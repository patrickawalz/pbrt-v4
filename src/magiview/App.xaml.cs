// (not in pbrt-v4) magiview/App.xaml.cs

using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace magiview
{
    public partial class App : Application
    {
        // App Public Methods

        // The program has no console: what the renderer writes of errors and warnings is kept, to be shown; and an exception
        // that nothing has caught is shown (and, where the program cannot go on, written to magiview-crash.txt beside it)
        // instead of the window closing without a word.
        public App()
        {
            Console.SetError(messages);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }

        // What the renderer has written since the last call
        public static string NewMessages()
        {
            string all;
            // (the writer that the threads share locks itself for each write; the text is in the one behind it)
            lock (messages)
            {
                all = buffer.ToString();
            }
            string added = all.Substring(Math.Min(shown, all.Length));
            shown = all.Length;
            return added;
        }

        // The text for a message box: its end, if it is long (the last lines say what went wrong)
        public static string Shorten(string text)
        {
            return text.Length <= 3000 ? text : "...\n" + text.Substring(text.Length - 3000);
        }

        // App Private Methods

        // An exception in the window's own code: the window stays
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(Shorten(NewMessages() + e.Exception.ToString()), "Magi", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        // An exception on another thread (one of the renderer's): the program ends after this
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            string report = NewMessages() + e.ExceptionObject.ToString();
            string file = Path.Combine(AppContext.BaseDirectory, "magiview-crash.txt");
            try
            {
                File.WriteAllText(file, report);
            }
            catch (Exception)
            {
                file = "(could not be written)";
            }
            MessageBox.Show(Shorten(report) + "\n\nThe whole report: " + file, "Magi has to close", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // App Private Members
        private static readonly StringWriter buffer = new StringWriter();
        private static readonly TextWriter messages = TextWriter.Synchronized(buffer);
        private static int shown;
    }
}
