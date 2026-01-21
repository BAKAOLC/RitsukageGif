using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace RitsukageGif
{
    /// <summary>
    ///     Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledExceptionHandler;
            DispatcherUnhandledException += App_DispatcherUnhandledExceptionHandler;
        }

        private static void CurrentDomain_UnhandledExceptionHandler(object sender,
            UnhandledExceptionEventArgs e)
        {
            ShowException(e.ExceptionObject as Exception);
        }

        private static void App_DispatcherUnhandledExceptionHandler(object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            ShowException(e.Exception);
            e.Handled = true;
        }

        private static void ShowException(Exception? ex)
        {
            try
            {
                RitsukageGif.MainWindow.ShutdownAllTasks();
            }
            catch
            {
                // ignored
            }

            var filename = $"RitsukageGif-Crash-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log";
            var sb = new StringBuilder();
            sb.AppendLine("程序发生了一个未处理的异常，请将以下信息反馈给开发者：");
            sb.AppendLine();
            var version = typeof(App).Assembly.GetName().Version;
            if (version != null)
                sb.AppendLine("程序集版本：").AppendLine(version.ToString());
            sb.AppendLine("异常类型：").AppendLine(ex?.GetType().FullName ?? "null");
            sb.AppendLine("异常消息：").AppendLine(ex?.Message ?? "null");
            sb.AppendLine("异常堆栈：").Append(ex?.StackTrace ?? "null");
            File.WriteAllText(filename, sb.ToString());
            var sb2 = new StringBuilder();
            sb2.AppendLine($"错误日志已保存到程序目录下，文件名为：{filename}");
            sb2.AppendLine();
            sb2.Append(sb);
            MessageBox.Show(sb2.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
        }
    }
}