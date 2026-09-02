using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;

namespace CommutingCheck
{
    public partial class Form1 : Form
    {
        public static IConfiguration config;

        public Form1()
        {
            InitializeComponent();

            config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            this.Load += Form1_Load;
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                await InitializeWebViewAsync();
                await WebLoginAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"자동 로그인 중 오류가 발생했습니다.\r\n\r\n{ex.Message}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private async Task InitializeWebViewAsync()
        {
            // WebView2 Core 초기화 완료까지 기다림
            await webView2.EnsureCoreWebView2Async();

            string url = config["AccountInfo:url"];

            if (string.IsNullOrWhiteSpace(url))
                throw new Exception("appsettings.json에 URL이 없습니다.");

            webView2.CoreWebView2.Navigate(url);

            // 로그인 화면의 userId가 생길 때까지 기다림
            bool loaded = await WaitForScriptConditionAsync(
                "document.getElementById('userId') !== null",
                timeoutMs: 15000
            );

            if (!loaded)
                throw new Exception("로그인 페이지를 불러오지 못했습니다.");
        }

        private async Task WebLoginAsync()
        {
            #region 혹시 몰라서 Edge 닫음

            CloseEdgeProcesses();

            #endregion

            #region 메인 로그인

            bool loginFieldsReady = await WaitForScriptConditionAsync(
                @"document.getElementById('userId') !== null &&
                  document.getElementById('userPw') !== null",
                timeoutMs: 15000
            );

            if (!loginFieldsReady)
                throw new Exception("아이디/비밀번호 입력창을 찾지 못했습니다.");

            string userId = EscapeJavaScriptString(
                config["AccountInfo:UserId"]
            );

            string password = EscapeJavaScriptString(
                config["AccountInfo:Password"]
            );

            await webView2.CoreWebView2.ExecuteScriptAsync(
                $"document.getElementById('userId').value = '{userId}';"
            );

            await webView2.CoreWebView2.ExecuteScriptAsync(
                $"document.getElementById('userPw').value = '{password}';"
            );

            bool actionLoginReady = await WaitForScriptConditionAsync(
                "typeof actionLogin === 'function'",
                timeoutMs: 10000
            );

            if (!actionLoginReady)
                throw new Exception("actionLogin 함수를 찾지 못했습니다.");

            await webView2.CoreWebView2.ExecuteScriptAsync(
                "actionLogin();"
            );

            #endregion

            #region 2차 인증

            bool secondLoginReady = await WaitForScriptConditionAsync(
                "typeof secondCertLogin === 'function'",
                timeoutMs: 15000
            );

            if (!secondLoginReady)
                throw new Exception("secondCertLogin 함수를 찾지 못했습니다.");

            await webView2.CoreWebView2.ExecuteScriptAsync(
                "secondCertLogin();"
            );

            #endregion

            #region 출퇴근 처리

            bool attendancePageReady = await WaitForScriptConditionAsync(
                @"document.getElementById('portletTemplete_mybox_tab1') !== null",
                timeoutMs: 20000
            );

            if (!attendancePageReady)
                throw new Exception("출퇴근 영역을 불러오지 못했습니다.");

            DateTime now = DateTime.Now;
            int dayOfWeek = Convert.ToInt32(now.DayOfWeek);
            int hour = now.Hour;

            // 월 ~ 금
            if (dayOfWeek >= 1 && dayOfWeek <= 5)
            {
                if (hour == 9 || hour == 19 || hour == 23)
                {
                    bool isIn = hour == 9;

                    if (isIn)
                    {
                        await ProcessCheckInAsync();
                    }
                    else
                    {
                        await ProcessCheckOutAsync();
                    }

                    // 확인 버튼이 생길 때까지 최대 5초 대기
                    bool confirmReady = await WaitForScriptConditionAsync(
                        "document.getElementById('btnConfirm') !== null",
                        timeoutMs: 5000
                    );

                    if (confirmReady)
                    {
                        await webView2.CoreWebView2.ExecuteScriptAsync(
                            "document.getElementById('btnConfirm').click();"
                        );
                    }
                }
            }

            #endregion

            // 마지막 JS 처리 후 약간만 기다림
            await Task.Delay(1000);

            CloseApplication();
        }

        private async Task ProcessCheckInAsync()
        {
            await webView2.CoreWebView2.ExecuteScriptAsync(
                @"
                var tab = document.getElementById('portletTemplete_mybox_tab1');

                if (tab && tab.innerHTML.indexOf('없음') > -1)
                {
                    fnAttendCheck(1, 0);
                }
                "
            );
        }

        private async Task ProcessCheckOutAsync()
        {
            bool tabsReady = await WaitForScriptConditionAsync(
                @"document.getElementById('portletTemplete_mybox_tab1') !== null &&
                  document.getElementById('portletTemplete_mybox_tab2') !== null",
                timeoutMs: 10000
            );

            if (!tabsReady)
                throw new Exception("퇴근 처리 영역을 찾지 못했습니다.");

            await webView2.CoreWebView2.ExecuteScriptAsync(
                @"
                document.getElementById('portletTemplete_mybox_tab1').style.display = 'none';
                document.getElementById('portletTemplete_mybox_tab2').style.display = 'block';

                var tab = document.getElementById('portletTemplete_mybox_tab2');

                if (tab && tab.innerHTML.indexOf('없음') > -1)
                {
                    fnAttendCheck(4, 0);
                }
                "
            );
        }

        private async Task<bool> WaitForScriptConditionAsync(
            string condition,
            int timeoutMs = 10000,
            int intervalMs = 500)
        {
            int elapsed = 0;

            while (elapsed < timeoutMs)
            {
                try
                {
                    string result =
                        await webView2.CoreWebView2.ExecuteScriptAsync(
                            $"Boolean({condition})"
                        );

                    if (result == "true")
                        return true;
                }
                catch
                {
                    // 페이지 이동 중에는 ExecuteScriptAsync 자체가
                    // 일시적으로 실패할 수 있으므로 다음 주기에 다시 시도
                }

                await Task.Delay(intervalMs);
                elapsed += intervalMs;
            }

            return false;
        }

        private void CloseEdgeProcesses()
        {
            try
            {
                Process[] processes =
                    Process.GetProcessesByName("msedge");

                foreach (Process process in processes)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Edge 종료 실패는 로그인 로직을 막지 않음
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // 필요 시 로그 처리
            }
        }

        private void CloseApplication()
        {
            Application.Exit();
        }

        private string EscapeJavaScriptString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}