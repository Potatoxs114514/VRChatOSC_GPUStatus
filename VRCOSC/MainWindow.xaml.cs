
using BuildSoft.VRChat.Osc.Chatbox;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Xml.Linq;
using Vortice.DXGI;

namespace VRCOSC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        int PCore = 8;
        int ECore = 12;
        string VRAM = "?";

        private string vrcAddress = "127.0.0.1";
        private int vrcPort = 9000;

        private string vramUsageText = "\r\nVRAM: Loading...";
        private string currentText = string.Empty;
        bool Close = false;

        private string strFilePath = "PSCFG";//获取INI文件路径
        private string strSec = "Record"; //INI节点名字
        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retval, int size, string filePath);

        public string CurrentText
        {
            get
            {
                return currentText;
            }
            set
            {
                currentText = value;
                OnPropertyChanged(nameof(CurrentText));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string ContentValue(string Section, string key)
        {
            StringBuilder temp = new StringBuilder(1024);
            GetPrivateProfileString(Section, key, "", temp, 1024, strFilePath);
            return temp.ToString();
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        public void SendOSCMessage()
        {
            //Start
            Send.Visibility = Visibility.Hidden;

            if (DXGI.CreateDXGIFactory1(out IDXGIFactory1 factory).Failure)
            {
                return;
            }
            using (factory)
            {
                for (int iAdapter = 0; ; iAdapter++)
                {
                    if (factory.EnumAdapters(iAdapter, out IDXGIAdapter adapter).Failure)
                    {
                        break;
                    }

                    using (adapter)
                    {
                        if (adapter.Description.DedicatedVideoMemory == 0)
                        {
                            continue;
                        }

                        VRAM = (int)(adapter.Description.DedicatedVideoMemory / 1024 / 1024 / 1000) + "";
                    }
                }
            }

            Thread thread = new Thread(() =>
            {
                while (!Close)
                {
                    Thread thread1 = new Thread(ShowChatBox);
                    thread1.Start();
                    Thread thread2 = new Thread(GetVRAMText);
                    thread2.Start();
                    Thread.Sleep(4000);
                }

            });
            thread.Start();
        }

        private void GetVRAMText()
        {
            try
            {
                var vramCounters = GetVRAMCounters();
                var vramUsage = GetVRAMUsage(vramCounters);
                vramUsage /= 1048576000;
                vramUsageText = "\r\nVRAM: " + vramUsage.ToString("F2") + " / " + VRAM + " GB丨FPS: ";
            }
            catch { }
        }

        private void ShowChatBox()
        {
            try
            {
                //GPU
                var gpuCounters = GetGPUCounters();
                var gpuUsage = GetGPUUsage(gpuCounters); if (gpuUsage > 99) gpuUsage = 99;
                var gpuBar = " ";
                for (int i = 1; i < gpuUsage; i += 11)
                {
                    gpuBar += "█";
                }
                while (gpuBar.Length < 10)
                {
                    gpuBar += "░";
                }
                gpuBar += "丨";
                var gpuText = "GPU:" + gpuBar + (int)gpuUsage + "%" + vramUsageText + File.ReadAllText("fps.txt");
                OscChatbox.SendMessage(gpuText, direct: true);
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ChatBox.Text = ex + "\r\n";
                });
            }

        }

        private void SendClick(object sender, RoutedEventArgs e)
        {
            using (Process process = new System.Diagnostics.Process())
            {
                process.StartInfo.FileName = "PresentMon-2.0.1-x64.exe";
                process.StartInfo.Arguments = "--stop_existing_session --process_name VRChat";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = false;
                process.Start(); 
            }

            SendOSCMessage();
        }

        public static List<PerformanceCounter> GetGPUCounters()
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var counterNames = category.GetInstanceNames();

            var gpuCounters = counterNames
                                .Where(counterName => counterName.EndsWith("engtype_3D"))
                                .SelectMany(counterName => category.GetCounters(counterName))
                                .Where(counter => counter.CounterName.Equals("Utilization Percentage"))
                                .ToList();

            return gpuCounters;
        }

        public static float GetGPUUsage(List<PerformanceCounter> gpuCounters)
        {
            gpuCounters.ForEach(x => x.NextValue());

            Thread.Sleep(1000);

            var result = gpuCounters.Sum(x => x.NextValue());

            return result;
        }

        public static List<PerformanceCounter> GetVRAMCounters()
        {
            var category = new PerformanceCounterCategory("GPU Adapter Memory");
            var counterNames = category.GetInstanceNames();
            var gpuCountersDedicated = new List<PerformanceCounter>();

            foreach (string counterName in counterNames)
            {
                foreach (var counter in category.GetCounters(counterName))
                {
                    if (counter.CounterName == "Dedicated Usage")
                    {
                        gpuCountersDedicated.Add(counter);
                    }
                    Thread.Sleep(50);
                }
            }

            return gpuCountersDedicated;
        }

        public static float GetVRAMUsage(List<PerformanceCounter> gpuCounters)
        {
            gpuCounters.ForEach(x => x.NextValue());

            Thread.Sleep(1000);

            var result = gpuCounters.Sum(x => x.NextValue());

            return result;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            Close = true;
        }


    }
}
