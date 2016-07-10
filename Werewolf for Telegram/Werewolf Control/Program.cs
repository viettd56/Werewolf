﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using Database;
using Werewolf_Control.Handler;
using Werewolf_Control.Helpers;

namespace Werewolf_Control
{
    class Program
    {
        internal static bool Running = true;
        private static bool _writingInfo = false;
        internal static PerformanceCounter CpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        internal static float AvgCpuTime;
        private static List<float> CpuTimes = new List<float>();
        private static List<long> MessagesProcessed = new List<long>();
        private static List<long> MessagesSent = new List<long>();
        private static long _previousMessages, _previousMessagesTx;
        internal static float MessageRxPerSecond;
        internal static float MessageTxPerSecond;
        internal static int NodeMessagesSent = 0;
        private static System.Timers.Timer _timer;
        static void Main(string[] args)
        {
#if !DEBUG
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                //drop the error to log file and exit
                using (var sw = new StreamWriter(Path.Combine(Bot.RootDirectory, "error.log"), true))
                {
                    var e = (eventArgs.ExceptionObject as Exception);
                    sw.WriteLine(e.Message);
                    sw.WriteLine(e.StackTrace);
                    if (eventArgs.IsTerminating)
                        Environment.Exit(5);
                }
            };
#endif
            //get the version of the bot and set the window title
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fvi.FileVersion;
            Console.Title = $"Werewolf Moderator {version}";

            //Initialize the TCP connections
            TCP.Initialize();
            //Let the nodes reconnect
            Thread.Sleep(1000);

            //start up the bot
            new Thread(Bot.Initialize).Start();
            new Thread(NodeMonitor).Start();
            new Thread(CpuMonitor).Start();
            //new Thread(MessageMonitor).Start();
            _timer = new System.Timers.Timer();
            _timer.Elapsed += new ElapsedEventHandler(TimerOnTick);
            _timer.Interval = 1000;
            _timer.Enabled = true;
            
            //now pause the main thread to let everything else run
            Thread.Sleep(-1);
        }

        private static void TimerOnTick(object sender, EventArgs eventArgs)
        {
            try
            {
                var newMessages = Bot.MessagesReceived - _previousMessages;
                _previousMessages = Bot.MessagesReceived;
                MessagesProcessed.Insert(0, newMessages);
                if (MessagesProcessed.Count > 60)
                    MessagesProcessed.RemoveAt(60);
                MessageRxPerSecond = MessagesProcessed.Max();

                newMessages = (Bot.MessagesSent + NodeMessagesSent) - _previousMessagesTx;
                _previousMessagesTx = (Bot.MessagesSent + NodeMessagesSent);
                MessagesSent.Insert(0, newMessages);
                if (MessagesSent.Count > 60)
                    MessagesSent.RemoveAt(60);
                MessageTxPerSecond = MessagesSent.Max();
            }
            catch
            {
                // ignored
            }
        }

        internal static string GetVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fvi.FileVersion;
            DateTime dt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Local).AddDays(fvi.ProductBuildPart).AddSeconds(fvi.ProductPrivatePart * 2).ToLocalTime();
            return "Current Version: " + version + Environment.NewLine + "Build time: " + dt + " (Central)";
        }

        public static void Log(string s, bool error = false)
        {
            while (_writingInfo)
                Thread.Sleep(50);
            Console.CursorTop = Math.Max(Console.CursorTop, 6 + Bot.Nodes.Count + 1);
            if (Console.CursorTop >= 30)
                Console.CursorTop = 19;
            Console.ForegroundColor = error ? ConsoleColor.Red : ConsoleColor.Gray;
            Console.WriteLine(s);
            try
            {
                using (var sw = new StreamWriter(Path.Combine(Bot.RootDirectory, "..\\Logs\\ControlLog.log"), true))
                {
                    sw.WriteLine($"{DateTime.Now} - {s}");
                }
            }
            catch
            {
                // ignored
            }
        }

        private static void MessageMonitor()
        {
            while (Running)
            {
                try
                {
                    var newMessages = Bot.MessagesReceived - _previousMessages;
                    _previousMessages = Bot.MessagesReceived;
                    MessagesProcessed.Insert(0, newMessages);
                    if (MessagesProcessed.Count > 10)
                        MessagesProcessed.RemoveAt(10);
                    MessageRxPerSecond = (float)MessagesProcessed.Average()/10;

                    newMessages = (Bot.MessagesSent + NodeMessagesSent) - _previousMessagesTx;
                    _previousMessagesTx = (Bot.MessagesSent + NodeMessagesSent);
                    MessagesSent.Insert(0, newMessages);
                    if (MessagesSent.Count > 10)
                        MessagesSent.RemoveAt(10);
                    MessageTxPerSecond = (float)MessagesSent.Average() / 10;
                }
                catch
                {
                    // ignored
                }

                Thread.Sleep(1000);
            }
        }

        private static void CpuMonitor()
        {
            while (Running)
            {
                try
                {
                    CpuTimes.Insert(0, CpuCounter.NextValue());
                    if (CpuTimes.Count > 10)
                        CpuTimes.RemoveAt(10);
                    AvgCpuTime = CpuTimes.Average();
                    
                }
                catch
                {
                    // ignored
                }

                Thread.Sleep(250);
            }
        }

        private static void NodeMonitor()
        {
            //wait a bit to allow nodes to register
            Thread.Sleep(6000);
            while (Running)
            {
                try
                {
                    var Nodes = Bot.Nodes.OrderBy(x => x.Version).ToList();
                    NodeMessagesSent = Nodes.Sum(x => x.MessagesSent);
                    var CurrentPlayers = Nodes.Sum(x => x.CurrentPlayers);
                    var CurrentGames = Nodes.Sum(x => x.CurrentGames);
                    var TotalPlayers = Nodes.Sum(x => x.TotalPlayers);
                    var TotalGames = Nodes.Sum(x => x.TotalGames);
                    var NumThreads = Process.GetCurrentProcess().Threads.Count;
                    var Uptime = DateTime.UtcNow - Bot.StartTime;
                    var MessagesRx = Bot.MessagesReceived;
                    var CommandsRx = Bot.CommandsReceived;
                    var MessagesTx = Nodes.Sum(x => x.MessagesSent) + Bot.MessagesSent;
                    var msg =
                        $"Connected Nodes: {Nodes.Count}  \nCurrent Players: {CurrentPlayers}  \tCurrent Games: {CurrentGames}  \nTotal Players: {TotalPlayers}  \tTotal Games: {TotalGames}  \n" +
                        $"Threads: {NumThreads}\tUptime: {Uptime}\nMessages Rx: {MessagesRx}\tCommands Rx: {CommandsRx}\tMessages Tx: {MessagesTx}\nMessages Per Second (IN): {MessageRxPerSecond}\tMessage Per Second (OUT): {MessageTxPerSecond}\n";
                    
                    msg = Nodes.Aggregate(msg, (current, n) => current + $"{(n.ShuttingDown ? "X " : "  ")}{n.ClientId} - {n.Version} - Games: {n.Games.Count}\t\n");

                    for (var i = 0; i < 12 - Nodes.Count; i++)
                        msg += new string(' ', Console.WindowWidth);


                    //now dump all this to the console
                    //first get our current caret position
                    _writingInfo = true;
                    var ypos = Math.Max(Console.CursorTop, 19);
                    if (ypos >= 30)
                        ypos = 19;
                    Console.CursorTop = 0;
                    var xpos = Console.CursorLeft;
                    Console.CursorLeft = 0;

                    //write the info
                    Console.WriteLine(msg);
                    //put the cursor back;
                    Console.CursorTop = ypos;
                    Console.CursorLeft = xpos;
                    _writingInfo = false;

                    //now, let's manage our nodes.
                    if (Nodes.All(x => x.Games.Count <= Settings.ShutDownNodesAt & !x.ShuttingDown) && Nodes.Count > 1)
                    {
                        //we have too many nodes running, kill one.
                        Nodes.First().ShutDown();
                    }

                    if (Nodes.Where(x => !x.ShuttingDown).All(x => x.Games.Count >= Settings.NewNodeThreshhold))
                    {
                        NewNode();
                        Thread.Sleep(5000); //give the node time to register
                    }

                    if (Nodes.All(x => x.ShuttingDown)) //replace nodes
                    {
                        NewNode();
                        Thread.Sleep(5000); //give the node time to register
                    }
                }
                finally
                {
                    _writingInfo = false;
                }
                Thread.Sleep(1000);
            }
        }

        private static void NewNode()
        {
            //all nodes have quite a few games, let's spin up another
            //this is a bit more tricky, we need to figure out which node folder has the latest version...
            var baseDirectory = Path.Combine(Bot.RootDirectory, ".."); //go up one directory
            var currentChoice = new NodeChoice();
            foreach (var dir in Directory.GetDirectories(baseDirectory, "*Node*"))
            {
                //get the node exe in this directory
                var file = Directory.GetFiles(dir, "Werewolf Node.exe").First();
                Version fvi = Version.Parse(FileVersionInfo.GetVersionInfo(file).FileVersion);
                if (fvi > currentChoice.Version)
                {
                    currentChoice.Path = file;
                    currentChoice.Version = fvi;
                }
            }

            //now we have the most recent version, launch one
            Process.Start(currentChoice.Path);
        }
    }

    class NodeChoice
    {
        public string Path { get; set; }
        public Version Version { get; set; } = Version.Parse("0.0.0.0");
    }
}
