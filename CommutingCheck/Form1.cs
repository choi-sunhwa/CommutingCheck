using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CommutingCheck
{
    public partial class Form1 : Form
    {
        public static IConfiguration config;
        public static IConfiguration workScheduleConfig;

        public Form1()
        {
            InitializeComponent();

            config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            workScheduleConfig = new ConfigurationBuilder()
                .AddJsonFile("workSchedule.json", optional: true)
                .Build();

            this.Load += Form1_Load;
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                if (!ShouldRunNow())
                {
                    CloseApplication();
                    return;
                }

                await InitializeWebViewAsync();
                await WebLoginAsync();
            }
            catch (Exception ex)
            {
                WriteLog($"ERROR: {ex.Message}");
                CloseApplication();
            }
        }

        private void WriteLog(string message)
        {
            string path = "CommutingCheck.log";

            System.IO.File.AppendAllText(
                path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}"
            );
        }

        private bool ShouldRunNow()
        {
            DateTime now = DateTime.Now;

            int dayOfWeek = Convert.ToInt32(now.DayOfWeek);
            int hour = now.Hour;

            // 주말이면 바로 종료
            if (dayOfWeek < 1 || dayOfWeek > 5)
                return false;

            string dateKey = now.ToString("yyyy-MM-dd");

            string scheduleType =
                workScheduleConfig[$"WorkSchedule:{dateKey}"];

            switch (scheduleType)
            {
                case "연차":
                    return false;

                case "오전":
                    // 오전반차
                    // 14:50 출근 / 19:01 퇴근
                    return hour == 14 || hour == 19;

                case "오후":
                    // 오후반차
                    // 09:40 출근 / 15:01 퇴근
                    return hour == 9 || hour == 15;

                default:
                    // 일반 근무
                    // 09:40 출근 / 19:01 퇴근
                    return hour == 9 || hour == 19;
            }
        }

        private async Task InitializeWebViewAsync()
        {
            // WebView2 Core 초기화 완료까지 기다림
            await webView2.EnsureCoreWebView2Async();

            webView2.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

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

        private void CoreWebView2_NewWindowRequested(
        object sender,
        CoreWebView2NewWindowRequestedEventArgs e)
            {
                e.Handled = true;
            }

        private async Task WebLoginAsync()
        {
            #region 혹시 몰라서 Edge 닫음

            //CloseEdgeProcesses();

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

            bool secondAuthReady = await WaitForScriptConditionAsync(
                @"document.getElementById('qrImg1') !== null &&
                  typeof secondCertLogin === 'function'",
                timeoutMs: 30000
            );

            if (!secondAuthReady)
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

            int hour = DateTime.Now.Hour;
            bool isIn = hour == 9 || hour == 14;

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
                    if (webView2.CoreWebView2 == null)
                    {
                        await Task.Delay(intervalMs);
                        elapsed += intervalMs;
                        continue;
                    }

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
            foreach (Form form in Application.OpenForms.Cast<Form>().ToList())
            {
                form.Close();
            }
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