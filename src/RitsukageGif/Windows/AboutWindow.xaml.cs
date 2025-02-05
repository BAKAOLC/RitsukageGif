﻿using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using RitsukageGif.Class;

namespace RitsukageGif.Windows
{
    /// <summary>
    ///     AboutWindow.xaml 的交互逻辑
    /// </summary>
    public partial class AboutWindow : Window
    {
        private static AboutWindow _instance;

        private static readonly string AboutHeaderText =
            $"""
             RitsukageGif ver {Assembly.GetExecutingAssembly().GetName().Version}
             构建日期: {BuildTimeText}

             {AboutText}
             """;

        private bool _closeFlag;

        public AboutWindow()
        {
            _instance = this;
            InitializeComponent();
        }

        private static string AboutText
        {
            get
            {
                using var stream = EmbeddedResourcesHelper.GetStream(new("embedded:///About.txt"));
                if (stream == null) return string.Empty;
                using var reader = new StreamReader(stream);
                return ReplaceHotKey(reader.ReadToEnd());
            }
        }

        private static string BuildTimeText
        {
            get
            {
                using var stream = EmbeddedResourcesHelper.GetStream(new("embedded:///BuildTime.txt"));
                if (stream == null) return string.Empty;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }

        private static string ReplaceHotKey(string text)
        {
            var instance = MainWindow.GetInstance();
            return instance == null
                ? text
                : Regex.Replace(text, @"\$[a-zA-Z0-9_]+\$", match =>
                {
                    return match.Value switch
                    {
                        "$$" => "$",
                        "$HotKey_RecordGif$" when instance.HotKeyPushRecordGif != default => instance
                            .HotKeyPushRecordGif.ToString(),
                        "$HotKey_SelectRegion$" when instance.HotKeySelectRegion != default => instance
                            .HotKeySelectRegion.ToString(),
                        "$HotKey_RecordGif$" or "$HotKey_SelectRegion$" => "未注册",
                        _ => match.Value,
                    };
                });
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AboutTextBox.Text = AboutHeaderText;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_closeFlag) return;
            e.Cancel = true;
            AboutTextBox.ScrollToHome();
            Hide();
        }

        private static AboutWindow GetInstance()
        {
            return _instance ??= new();
        }

        public static AboutWindow Begin()
        {
            var instance = GetInstance();
            instance.Show();
            return instance;
        }

        public static void CloseInstance()
        {
            if (_instance == null) return;
            _instance._closeFlag = true;
            _instance.Close();
            _instance = null;
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}