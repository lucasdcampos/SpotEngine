// Copyright (c) Trivalent Studios
// Licensed under MIT License.

namespace SpotEngine
{
    /// <summary>
    /// A class for managing logging and displaying messages with optional formatting and coloring.
    /// </summary>
    public class Log
    {
        public static string format = "%TS %PR %MSG";
        public static string prefix = "SPOT";

        /// <summary>
        /// Logs a message with default formatting and color.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Info(string message)
        {
            prefix = "INFO";
            ConsoleWriteLine(message, ConsoleColor.White);
        }

        /// <summary>
        /// Logs a warning message with a custom color.
        /// </summary>
        /// <param name="message">The warning message to log.</param>
        public static void Warn(string message)
        {
            prefix = "WARN";
            ConsoleWriteLine(message, ConsoleColor.DarkYellow);
        }

        /// <summary>
        /// Logs an error message with a custom color.
        /// </summary>
        /// <param name="message">The error message to log.</param>
        public static void Error(string message)
        {
            prefix = "ERROR";
            ConsoleWriteLine(message, ConsoleColor.DarkRed);
        }

        /// <summary>
        /// Log a custom Dev message (Will be displayed only when Application.debugMode is true)
        /// </summary>
        /// <param name="message"></param>
        public static void Dev(string message)
        {
            //if (!Application.DebugMode) return;

            prefix = "DEV";
            ConsoleWriteLine(message, ConsoleColor.DarkGreen);

        }

        /// <summary>
        /// Logs a custom message with a specified color.
        /// </summary>
        /// <param name="message">The custom message to log.</param>
        /// <param name="color">The custom color for the message.</param>
        public static void Custom(string message, ConsoleColor color)
        {
            prefix = "INFO";
            ConsoleWriteLine(message, color);
        }

        private static void ConsoleWriteLine(string message, ConsoleColor color)
        {
            string formattedMessage = format.Replace("%TS", $"[{DateTime.Now.ToString("HH:mm:ss")}]")
                                            .Replace("%PR", prefix + ":")
                                            .Replace("%MSG", message);

            Console.ForegroundColor = color;
            Console.WriteLine(formattedMessage);
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
