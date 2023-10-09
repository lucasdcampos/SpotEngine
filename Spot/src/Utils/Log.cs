// Copyright (c) Trivalent Studios
// Licensed under MIT License.

namespace SpotEngine
{
    public class Log
    {
        public static string format = "%TS %PR %MSG";
        public static string prefix = "SPOT";
        public static void Message(string message)
        {
            ConsoleWriteLine(message, ConsoleColor.White);
        }

        public static void Warn(string message)
        {
            ConsoleWriteLine(message, ConsoleColor.DarkYellow);
        }

        public static void Error(string message)
        {
            ConsoleWriteLine(message,ConsoleColor.DarkRed);
        }

        public static void Custom(string message, ConsoleColor color)
        {
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
