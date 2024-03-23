﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Net;
using Path = System.IO.Path;

namespace SC2Shelter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly (string, string, bool)[] Langs = 
        {
            ("zhCN", "简体中文", true),
            ("zhTW", "繁体中文", true),
            ("deDE", "德语", true),
            ("enUS", "英语", true),
            ("esMX", "西班牙语(墨西哥)", true),
            ("esES", "西班牙语(西班牙)", true),
            ("frFR", "法语", true),
            ("itIT", "意大利语", true),
            ("plPL", "波兰语", true),
            ("ptBR", "葡萄牙语(巴西)", true),
            ("ruRU", "俄语", true),
            ("koKR", "朝鲜语(南朝鲜)", true)
        };
        const string save = "latest.list";
        private static readonly object LockerFile = new object();
        private static readonly object LockerConsole = new object();
        private static List<CheckBox> LangBoxes = new List<CheckBox>();
        private const string CacheDir = "C:/ProgramData/Blizzard Entertainment/Battle.net/Cache/";
        private long Version = 0L;
        private bool NeedRefresh = false;
        SolidColorBrush BrushRed = new SolidColorBrush(Color.FromArgb(255, 255, 182, 193));
        SolidColorBrush BrushYellow = new SolidColorBrush(Color.FromArgb(255, 255, 255, 180));
        SolidColorBrush BrushGreen = new SolidColorBrush(Color.FromArgb(255, 180, 255, 180));
        private const string StateSafe = "安全，已锁住带有\n链接的地图信息";
        private const string StateWarn = "仅勾选的语言安全";
        private const string StateUnsafe = "不安全，游戏可能卡死!\n请检先关闭游戏,等到显示安全再启动。";
        private static readonly List<(string, string)> BlockList = new List<(string, string)>();

        private static readonly Dictionary<string, FileStream> LockedFiles = new Dictionary<string, FileStream>();
        private static bool LockFile(string filePath)
        {
            lock (LockerFile)
            {
                if (LockedFiles.ContainsKey(filePath)) return true;
                try
                {
                    var directoryPath = Path.GetDirectoryName(filePath);
                    if (!Directory.Exists(directoryPath) && directoryPath != null) Directory.CreateDirectory(directoryPath);
                    if (!File.Exists(filePath)) File.Create(filePath).Close();
                    var fileStream = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    fileStream.Lock(0, 1);
                    LockedFiles[filePath] = fileStream;
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            }
        }
        private bool UnlockFile(string filePath)
        {
            lock (LockerFile)
            {
                if (!LockedFiles.ContainsKey(filePath)) return true;
                try
                {
                    var fileStream = LockedFiles[filePath];
                    fileStream.Unlock(0, fileStream.Length);
                    fileStream.Dispose();
                    LockedFiles.Remove(filePath);
                    return true;
                }
                catch (IOException e)
                {
                    return false;
                }
            }
        }
        public MainWindow()
        {
            InitializeComponent();
            AddCheckboxes();
            ReadSaving();
            RunAsyncTask();
            UpdateList();
        }
        private void AddCheckboxes()
        {
            foreach (var (id, text, state) in Langs)
            {
                var checkBox = new CheckBox
                {
                    Content = text,
                    IsChecked = state
                };
                checkBox.Checked += Checked;
                checkBox.Unchecked += Unchecked;
                LangPanel.Children.Add(checkBox);
                LangBoxes.Add(checkBox);
            }
        }

        private void Print(string text)
        {
            Dispatcher.Invoke(() =>
            {
                lock (LockerConsole)
                {
                    ConsoleBox.AppendText(text + "\n");
                    if(Math.Abs(ConsoleBoxViewer.ScrollableHeight - ConsoleBoxViewer.VerticalOffset) < 0.01)
                        ConsoleBoxViewer.ScrollToBottom();
                }
            });
        }

        private void Checked(object sender, RoutedEventArgs e)
        {
            var index = LangBoxes.IndexOf((CheckBox)sender);
            if (index >= 0)
            {
                var (id, text, _) = Langs[index];
                Langs[index] = (id, text, true);
            }
            NeedRefresh = true;
        }

        private void Unchecked(object sender, RoutedEventArgs e)
        {
            var index = LangBoxes.IndexOf((CheckBox)sender);
            if (index >= 0)
            {
                var (id, text, _) = Langs[index];
                Langs[index] = (id, text, false);
            }
            NeedRefresh = true;
        }

        private bool LangLock(string lang)
        {
            foreach (var (id, _, state) in Langs)
            {
                if (id == lang) return state;
            }
            return false;
        }
        private async void RunAsyncTask()
        {
            await Task.Run(() =>
                {
                    while (true)
                    {
                        if (NeedRefresh)
                        {
                            var safe = true;
                            var lockCount = 0;
                            var unlockCount = 0;
                            var failLock = 0;
                            var failUnlock = 0;
                            foreach (var (name, lang) in BlockList)
                            {
                                var path = CacheDir + name;
                                var toLock = LangLock(lang);
                                if (!LockedFiles.ContainsKey(path))
                                {
                                    if (toLock)
                                    {
                                        var result = LockFile(CacheDir + name);
                                        if (result)
                                        {
                                            lockCount++;
                                        }
                                        else
                                        {
                                            failLock++;
                                            safe = false;
                                        }
                                    }
                                }
                                else
                                {
                                    if (!toLock)
                                    {
                                        var result = UnlockFile(CacheDir + name);
                                        if (result)
                                        {
                                            unlockCount++;
                                        }
                                        else
                                        {
                                            failUnlock++;
                                        }
                                    }
                                }
                            }

                            var info = "文件状态更新完毕";
                            if (lockCount > 0) info += $"，{lockCount}个文件被锁定";
                            if (failLock > 0) info += $"，{failLock}个文件锁定失败";
                            if (unlockCount > 0) info += $"，{unlockCount}个文件被解锁";
                            if (failUnlock > 0) info += $"，{failUnlock}个文件解锁失败";
                            if (lockCount == 0 && failLock == 0 && unlockCount == 0 && failUnlock == 0)
                                info += "，没有文件发生状态更新。";
                            else
                                info += "。";
                            Print(info);
                            NeedRefresh = false;
                            Dispatcher.Invoke(() =>
                            {
                                if (safe)
                                {
                                    var lockAll = true;
                                    foreach (var (_, _, state) in Langs)
                                    {
                                        if (!state)
                                        {
                                            lockAll = false;
                                            break;
                                        }
                                    }

                                    if (lockAll)
                                    {
                                        StateLabel.Background = BrushGreen;
                                        StateLabel.Content = StateSafe;
                                    }
                                    else
                                    {
                                        StateLabel.Background = BrushYellow;
                                        StateLabel.Content = StateWarn; 
                                    }
                                }
                                else
                                {
                                    StateLabel.Background = BrushRed;
                                    StateLabel.Content = StateUnsafe;
                                }
                            });
                            
                        }
                        Task.Delay(50).Wait();
                    }
                }
            );
        }

        private async void UpdateList()
        {
            await Task.Run(() =>
                {
                    while (true)
                    {
                        try
                        {
                            var response = WebRequest.Create("###############").GetResponse();
                            var stream = response.GetResponseStream();
                            var bytes = new byte[16];
                            if (stream != null)
                            {
                                var latest = 0L;
                                if (stream.Read(bytes) == 16)
                                {
                                    for (var i = 8; i < 16; i++)
                                    {
                                        latest <<= 8;
                                        latest += bytes[i];
                                    }
                                }
                                stream.Close();
                                if (latest != Version)
                                {
                                    if (File.Exists(save)) File.Delete(save);
                                    new WebClient().DownloadFile("("###############").", save);
                                    ReadSaving();
                                    Version = latest;
                                    Print("已获取最新屏蔽列表！");
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                        try
                        {
                            var response = WebRequest.Create("("###############").").GetResponse();
                            var stream = response.GetResponseStream();
                            var bytes = new byte[4];
                            if (stream != null)
                            {
                                var users = 0;
                                if (stream.Read(bytes) == 4)
                                {
                                    for (var i = 0; i < 4; i++)
                                    {
                                        users <<= 8;
                                        users += bytes[i];
                                    }
                                }
                                Dispatcher.Invoke(() =>
                                {
                                    UsersLabel.Content = $"{users}人正在同时使用";
                                });
                            }
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                        Task.Delay(10000).Wait();
                    }
                }
            );
        }

        private void ReadSaving()
        {
            try
            {
                var buffer = new List<(string, string)>();
                foreach (var line in File.ReadAllLines(save))
                {
                    var pars = line.Split(';');
                    buffer.Add((pars[0], pars[1]));
                }
                BlockList.Clear();
                foreach (var pair in buffer)
                {
                    BlockList.Add(pair);
                }
                NeedRefresh = true;
            }
            catch
            {
                // ignored
            }
        }
    }
}