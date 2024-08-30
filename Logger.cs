// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;

namespace osu.Desktop.Deploy
{
    public static class Logger
    {
        private static readonly Stopwatch stopwatch = Stopwatch.StartNew();

        public static void Error(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FATAL ERROR: {message}");
            Console.ResetColor();

            Program.PauseIfInteractive();
            Environment.Exit(-1);
        }

        public static void Write(string message, ConsoleColor col = ConsoleColor.Gray)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(stopwatch.ElapsedMilliseconds.ToString().PadRight(8));

            Console.ForegroundColor = col;
            Console.WriteLine(message);
        }
    }
}
