using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;

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

            webView2.Source = new System.Uri($"{config["AccountInfo:url"]}", System.UriKind.Absolute);

            WebLogin();
        }

        private async void WebLogin()
        {
            #region 혹시 몰라서 edge 닫음
            await Task.Delay(1500);
            Process[] mProcess = Process.GetProcessesByName("msedge");
            if(mProcess.Length > 0) foreach (Process process in mProcess) process.Kill();
            #endregion 혹시 몰라서 edge 닫음

            #region 메인 로그인
            await Task.Delay(1500);
            webView2?.CoreWebView2.ExecuteScriptAsync($"document.getElementById('userId').value = '{config["AccountInfo:UserId"]}'");
            webView2?.CoreWebView2.ExecuteScriptAsync($"document.getElementById('userPw').value = '{config["AccountInfo:Password"]}'");

            await Task.Delay(1500);
            await webView2.CoreWebView2.ExecuteScriptAsync("actionLogin()");
            #endregion 메인 로그인

            await Task.Delay(1500);
            await webView2.CoreWebView2.ExecuteScriptAsync("secondCertLogin()");

            await Task.Delay(5000);
            DateTime now = DateTime.Now;
            int dayOfWeek = Convert.ToInt32(now.DayOfWeek);
            int hour = now.Hour;
            if (dayOfWeek >= 1 && dayOfWeek <= 5) //월화수목금
            {
                if (hour == 9 || hour == 19 || hour == 23)
                {
                    bool _in = true;
                    if (hour == 19 || hour == 23) _in = false;

                    if (_in) webView2?.CoreWebView2.ExecuteScriptAsync("if(document.getElementById('portletTemplete_mybox_tab1').innerHTML.indexOf('없음') > -1) fnAttendCheck(1,0);");
                    else
                    {
                        webView2?.CoreWebView2.ExecuteScriptAsync("document.getElementById('portletTemplete_mybox_tab1').style.display = 'none';");
                        webView2?.CoreWebView2.ExecuteScriptAsync("document.getElementById('portletTemplete_mybox_tab2').style.display = 'block';");
                        webView2?.CoreWebView2.ExecuteScriptAsync("if(document.getElementById('portletTemplete_mybox_tab2').innerHTML.indexOf('없음') > -1) fnAttendCheck(4,0);");
                    }

                    await Task.Delay(1500);
                    webView2?.CoreWebView2.ExecuteScriptAsync("if(document.getElementById('btnConfirm')) document.getElementById('btnConfirm').click();");
                }
            }

            await Task.Delay(1500);
            mProcess = Process.GetProcessesByName("CommutingCheck");
            if (mProcess.Length == 1)
            {
                foreach (Process process in mProcess) process.Kill();
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }
    }
}
