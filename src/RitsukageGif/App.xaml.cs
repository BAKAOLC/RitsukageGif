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
    public partial class App : Application
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

        private static void ShowException(Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine("程序发生了一个未处理的异常，请将以下信息反馈给开发者：");
            sb.AppendLine();
            sb.AppendLine($"异常类型：{ex?.GetType().FullName ?? "null"}");
            sb.AppendLine($"异常消息：{ex?.Message ?? "null"}");
            sb.AppendLine($"异常堆栈：{ex?.StackTrace ?? "null"}");
            File.WriteAllText($"RitsukageGif-Crash-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log", sb.ToString());
            sb.Insert(0, $"错误日志已保存到程序目录下，文件名为：RitsukageGif-Crash-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log");
            sb.AppendLine();

            RitsukageGif.MainWindow.ShutdownAllTasks();
            MessageBox.Show(sb.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown();
        }
    }
}