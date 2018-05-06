using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NfcServer
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private HttpListener listener;

        public static string Code { get; set; }

        public static string QrResponce
        {
            get
            {
                return "<html><head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Успех!</title></head><body><h3>Считывание прошло успешно!</h3><p>Через некоторое время данные поступят в 1С...</p></body></html>";
            }
        }

        private void WriteText(HttpListenerContext ctx, string text)
        {
            byte[] str = Encoding.Default.GetBytes(text);
            MemoryStream ms = new MemoryStream();
            ms.Write(str, 0, str.Length);

            var response = ctx.Response;
            response.ContentLength64 = ms.Length;
            response.SendChunked = false;
            response.ContentType = System.Net.Mime.MediaTypeNames.Text.Html;
            response.ContentEncoding = Encoding.Default;
            response.AppendHeader("Access-Control-Allow-Origin", "*");

            using (BinaryWriter sw = new BinaryWriter(response.OutputStream))
            {
                sw.Write(ms.ToArray());
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            response.StatusDescription = "OK";
            response.OutputStream.Close();
        }

        public MainWindow()
        {
            InitializeComponent();
            Code = string.Empty;
            Task.Run(() =>
            {
                Thread.Sleep(750);
                string lanadderss = string.Empty;

                NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface adapter in adapters)
                {
                    IPInterfaceProperties properties = adapter.GetIPProperties();
                    if (properties.DnsSuffix == "lan")
                    {
                        foreach (UnicastIPAddressInformation ip in properties.UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                lanadderss=ip.Address.ToString();
                            }
                        }
                    }
                }
                if (lanadderss == string.Empty)
                {
                    Interaction.MsgBox("Не удается определить текущий IPv4 адресс компьютера, доступный из локальной сети! Программа будет закрыта.");
                    App.Current.Dispatcher.Invoke(() => App.Current.Shutdown());
                    return;
                }

                int port = 8080;
                string baseurl = string.Format("http://{0}:{1}/", lanadderss,port);
                listener = new HttpListener();
                listener.Prefixes.Add(baseurl);

                try
                {
                    listener.Start();
                }
                catch (HttpListenerException)
                {
                    //Задать запись в реестр для раздачи порта
                    ProcessStartInfo startInfo = new ProcessStartInfo("cmd.exe");
                    startInfo.Verb = "runas";
                    startInfo.Arguments = "/c netsh http add urlacl url="+ baseurl + " user=".Replace("'", "\"") + System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                    try
                    {
                        Process.Start(startInfo);
                    }
                    catch(Exception ex)
                    {
                        Interaction.MsgBox("Не удается задать правило фаервола для сетевого взаимодействия. Возможно, вы отказались от предоставления прав администратора! Ошибка:" + ex.Message);
                    }
                    App.Current.Dispatcher.Invoke(() => App.Current.Shutdown());
                    return;
                }

                Task.Run(() =>
                {
                    while (true)
                    {
                        HttpListenerContext context = listener.GetContext();
                        HttpListenerRequest request = context.Request;

                        NameValueCollection parameters = request.QueryString;
                        Task.Factory.StartNew((ctx) =>
                        {
                            if (!string.IsNullOrWhiteSpace(parameters["Qr"]))
                            {
                                Code= parameters["Qr"];
                                WriteText((HttpListenerContext)ctx, QrResponce);
                            }
                            else if (!string.IsNullOrWhiteSpace(parameters["State"]))
                            {
                                WriteText((HttpListenerContext)ctx, Code==string.Empty?"NotReady":"Ready");
                            }
                            else if (!string.IsNullOrWhiteSpace(parameters["Code"]))
                            {
                                WriteText((HttpListenerContext)ctx, Code);
                                App.Current.Dispatcher.Invoke(() => App.Current.Shutdown());
                            }
                            else
                            {
                                WriteText((HttpListenerContext)ctx, "omg");
                            }
                        }, context, TaskCreationOptions.LongRunning);
                    }
                });

                App.Current.Dispatcher.Invoke(() => QrCodeImage.Source = App.ConvertToBitmapSource(App.GenerateQRCode(baseurl)));
            });
        }
    }
}
