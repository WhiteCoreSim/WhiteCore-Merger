/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Timers;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Statistics;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim
{
    /// <summary>
    /// Interactive OpenSim region server
    /// </summary>
    public class OpenSim : OpenSimBase
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected string m_startupCommandsFile;
        protected string m_shutdownCommandsFile;
        protected bool m_gui = false;

        private string m_timedScript = "disabled";
        private Timer m_scriptTimer;

        public OpenSim(IConfigSource configSource) : base(configSource)
        {
        }

        protected override void ReadExtraConfigSettings()
        {
            base.ReadExtraConfigSettings();

            IConfig startupConfig = m_config.Source.Configs["Startup"];

            if (startupConfig != null)
            {
                m_startupCommandsFile = startupConfig.GetString("startup_console_commands_file", "startup_commands.txt");
                m_shutdownCommandsFile = startupConfig.GetString("shutdown_console_commands_file", "shutdown_commands.txt");

                m_gui = startupConfig.GetBoolean("gui", false);

                m_timedScript = startupConfig.GetString("timer_Script", "disabled");
                if (m_logFileAppender != null)
                {
                    if (m_logFileAppender is log4net.Appender.FileAppender)
                    {
                        log4net.Appender.FileAppender appender =
                                (log4net.Appender.FileAppender)m_logFileAppender;
                        string fileName = startupConfig.GetString("LogFile", String.Empty);
                        if (fileName != String.Empty)
                            appender.File = fileName;
                        m_log.InfoFormat("[LOGGING] Logging started to file {0}", appender.File);
                    }
                }
            }
        }

        /// <summary>
        /// Performs initialisation of the scene, such as loading configuration from disk.
        /// </summary>
        protected override void StartupSpecific()
        {
            m_log.Info("====================================================================");
            m_log.Info("========================= STARTING OPENSIM =========================");
            m_log.Info("====================================================================");
            m_log.InfoFormat("[OPENSIM MAIN]: Running in {0} mode",
                             (ConfigurationSettings.Standalone ? "sandbox" : "grid"));
            //m_log.InfoFormat("[OPENSIM MAIN]: GC Is Server GC: {0}", GCSettings.IsServerGC.ToString());
            // http://msdn.microsoft.com/en-us/library/bb384202.aspx
            //GCSettings.LatencyMode = GCLatencyMode.Batch;
            //m_log.InfoFormat("[OPENSIM MAIN]: GC Latency Mode: {0}", GCSettings.LatencyMode.ToString());

            if (m_gui) // Driven by external GUI
                m_console = new CommandConsole("Region");
            else
                m_console = new LocalConsole("Region");
            MainConsole.Instance = m_console;

            RegisterConsoleCommands();

            base.StartupSpecific();

            //Run Startup Commands
            if (String.IsNullOrEmpty(m_startupCommandsFile))
            {
                m_log.Info("[STARTUP]: No startup command script specified. Moving on...");
            }
            else
            {
                RunCommandScript(m_startupCommandsFile);
            }

            // Start timer script (run a script every xx seconds)
            if (m_timedScript != "disabled")
            {
                m_scriptTimer = new Timer();
                m_scriptTimer.Enabled = true;
                m_scriptTimer.Interval = 1200*1000;
                m_scriptTimer.Elapsed += RunAutoTimerScript;
            }

            PrintFileToConsole("startuplogo.txt");

            // For now, start at the 'root' level by default
            if (m_sceneManager.Scenes.Count == 1) // If there is only one region, select it
                ChangeSelectedRegion("region",
                                     new string[] {"change", "region", m_sceneManager.Scenes[0].RegionInfo.RegionName});
            else
                ChangeSelectedRegion("region", new string[] {"change", "region", "root"});
        }

        private void RegisterConsoleCommands()
        {
            m_console.Commands.AddCommand("region", false, "clear assets",
                                          "clear assets",
                                          "Clear the asset cache", HandleClearAssets);

            m_console.Commands.AddCommand("region", false, "force update",
                                          "force update",
                                          "Force the update of all objects on clients",
                                          HandleForceUpdate);

            m_console.Commands.AddCommand("region", false, "debug packet",
                                          "debug packet <level>",
                                          "Turn on packet debugging", Debug);

            m_console.Commands.AddCommand("region", false, "debug scene",
                                          "debug scene <cripting> <collisions> <physics>",
                                          "Turn on scene debugging", Debug);

            m_console.Commands.AddCommand("region", false, "change region",
                                          "change region <region name>",
                                          "Change current console region", ChangeSelectedRegion);

            m_console.Commands.AddCommand("region", false, "save xml",
                                          "save xml",
                                          "Save a region's data in XML format", SaveXml);

            m_console.Commands.AddCommand("region", false, "save xml2",
                                          "save xml2",
                                          "Save a region's data in XML2 format", SaveXml2);

            m_console.Commands.AddCommand("region", false, "load xml",
                                          "load xml [-newIDs [<x> <y> <z>]]",
                                          "Load a region's data from XML format", LoadXml);

            m_console.Commands.AddCommand("region", false, "load xml2",
                                          "load xml2",
                                          "Load a region's data from XML2 format", LoadXml2);

            m_console.Commands.AddCommand("region", false, "save prims xml2",
                                          "save prims xml2 [<prim name> <file name>]",
                                          "Save named prim to XML2", SavePrimsXml2);

            m_console.Commands.AddCommand("region", false, "load oar",
                                          "load oar <oar name>",
                                          "Load a region's data from OAR archive", LoadOar);

            m_console.Commands.AddCommand("region", false, "save oar",
                                          "save oar <oar name>",
                                          "Save a region's data to an OAR archive",
                                          "More information on forthcoming options here soon", SaveOar);

            m_console.Commands.AddCommand("region", false, "edit scale",
                                          "edit scale <name> <x> <y> <z>",
                                          "Change the scale of a named prim", HandleEditScale);

            m_console.Commands.AddCommand("region", false, "kick user",
                                          "kick user <first> <last> [message]",
                                          "Kick a user off the simulator", KickUserCommand);

            m_console.Commands.AddCommand("region", false, "show assets",
                                          "show assets",
                                          "Show asset data", HandleShow);

            m_console.Commands.AddCommand("region", false, "show users",
                                          "show users [full]",
                                          "Show user data", HandleShow);

            m_console.Commands.AddCommand("region", false, "show users full",
                                          "show users full",
                                          String.Empty, HandleShow);

            m_console.Commands.AddCommand("region", false, "show modules",
                                          "show modules",
                                          "Show module data", HandleShow);

            m_console.Commands.AddCommand("region", false, "show regions",
                                          "show regions",
                                          "Show region data", HandleShow);

            m_console.Commands.AddCommand("region", false, "show queues",
                                          "show queues",
                                          "Show queue data", HandleShow);
            m_console.Commands.AddCommand("region", false, "show ratings",
                                          "show ratings",
                                          "Show rating data", HandleShow);

            m_console.Commands.AddCommand("region", false, "backup",
                                          "backup",
                                          "Persist objects to the database now", RunCommand);

            m_console.Commands.AddCommand("region", false, "create region",
                                          "create region",
                                          "Create a new region", HandleCreateRegion);

            m_console.Commands.AddCommand("region", false, "login enable",
                                          "login enable",
                                          "Enable logins to the simulator", HandleLoginEnable);

            m_console.Commands.AddCommand("region", false, "login disable",
                                          "login disable",
                                          "Disable logins to the simulator", HandleLoginDisable);

            m_console.Commands.AddCommand("region", false, "login status",
                                          "login status",
                                          "Display status of logins", HandleLoginStatus);

            m_console.Commands.AddCommand("region", false, "restart",
                                          "restart",
                                          "Restart all sims in this instance", RunCommand);

            m_console.Commands.AddCommand("region", false, "config set",
                                          "config set <section> <field> <value>",
                                          "Set a config option", HandleConfig);

            m_console.Commands.AddCommand("region", false, "config get",
                                          "config get <section> <field>",
                                          "Read a config option", HandleConfig);

            m_console.Commands.AddCommand("region", false, "config save",
                                          "config save",
                                          "Save current configuration", HandleConfig);

            m_console.Commands.AddCommand("region", false, "command-script",
                                          "command-script <script>",
                                          "Run a command script from file", RunCommand);

            m_console.Commands.AddCommand("region", false, "remove-region",
                                          "remove-region <name>",
                                          "Remove a region from this simulator", RunCommand);

            m_console.Commands.AddCommand("region", false, "delete-region",
                                          "delete-region <name>",
                                          "Delete a region from disk", RunCommand);

            m_console.Commands.AddCommand("region", false, "predecode-j2k",
                                          "predecode-j2k [<num threads>]>",
                                          "Precache assets,decode j2k layerdata", RunCommand);

            m_console.Commands.AddCommand("region", false, "modules list",
                                          "modules list",
                                          "List modules", HandleModules);

            m_console.Commands.AddCommand("region", false, "modules load",
                                          "modules load <name>",
                                          "Load a module", HandleModules);

            m_console.Commands.AddCommand("region", false, "modules unload",
                                          "modules unload <name>",
                                          "Unload a module", HandleModules);

            m_console.Commands.AddCommand("region", false, "Add-InventoryHost",
                                          "Add-InventoryHost <host>",
                                          String.Empty, RunCommand);

            if (ConfigurationSettings.Standalone)
            {
                m_console.Commands.AddCommand("region", false, "create user",
                                              "create user [<first> [<last> [<pass> [<x> <y> [<email>]]]]]",
                                              "Create a new user", HandleCreateUser);

                m_console.Commands.AddCommand("region", false, "reset user password",
                                              "reset user password [<first> [<last> [<password>]]]",
                                              "Reset a user password", HandleResetUserPassword);
            }

            m_console.Commands.AddCommand("hypergrid", false, "link-mapping", "link-mapping [<x> <y>] <cr>",
                                          "Set local coordinate to map HG regions to", RunCommand);
            m_console.Commands.AddCommand("hypergrid", false, "link-region",
                                          "link-region <Xloc> <Yloc> <HostName>:<HttpPort>[:<RemoteRegionName>] <cr>",
                                          "Link a hypergrid region", RunCommand);
            m_console.Commands.AddCommand("hypergrid", false, "unlink-region",
                                          "unlink-region <local name> or <HostName>:<HttpPort> <cr>",
                                          "Unlink a hypergrid region", RunCommand);

        }

        public override void ShutdownSpecific()
        {
            if (m_shutdownCommandsFile != String.Empty)
            {
                RunCommandScript(m_shutdownCommandsFile);
            }
            base.ShutdownSpecific();
        }

        private void RunAutoTimerScript(object sender, EventArgs e)
        {
            if (m_timedScript != "disabled")
            {
                RunCommandScript(m_timedScript);
            }
        }

        #region Console Commands

        private void KickUserCommand(string module, string[] cmdparams)
        {
            if (cmdparams.Length < 4)
                return;

            string alert = null;
            if (cmdparams.Length > 4)
                alert = String.Format("\n{0}\n", String.Join(" ", cmdparams, 4, cmdparams.Length - 4));

            IList agents = m_sceneManager.GetCurrentSceneAvatars();

            foreach (ScenePresence presence in agents)
            {
                RegionInfo regionInfo = m_sceneManager.GetRegionInfo(presence.RegionHandle);

                if (presence.Firstname.ToLower().Contains(cmdparams[2].ToLower()) &&
                    presence.Lastname.ToLower().Contains(cmdparams[3].ToLower()))
                {
                    m_console.Notice(
                        String.Format(
                            "Kicking user: {0,-16}{1,-16}{2,-37} in region: {3,-16}",
                            presence.Firstname, presence.Lastname, presence.UUID, regionInfo.RegionName));

                    // kick client...
                    if (alert != null)
                        presence.ControllingClient.Kick(alert);
                    else 
                        presence.ControllingClient.Kick("\nThe OpenSim manager kicked you out.\n");

                    // ...and close on our side
                    presence.Scene.IncomingCloseAgent(presence.UUID);
                }
            }
            m_console.Notice("");
        }

        /// <summary>
        /// Run an optional startup list of commands
        /// </summary>
        /// <param name="fileName"></param>
        private void RunCommandScript(string fileName)
        {
            if (File.Exists(fileName))
            {
                m_log.Info("[COMMANDFILE]: Running " + fileName);

                StreamReader readFile = File.OpenText(fileName);
                string currentCommand;
                while ((currentCommand = readFile.ReadLine()) != null)
                {
                    if (currentCommand != String.Empty)
                    {
                        m_log.Info("[COMMANDFILE]: Running '" + currentCommand + "'");
                        m_console.RunCommand(currentCommand);
                    }
                }
            }
        }

        private static void PrintFileToConsole(string fileName)
        {
            if (File.Exists(fileName))
            {
                StreamReader readFile = File.OpenText(fileName);
                string currentLine;
                while ((currentLine = readFile.ReadLine()) != null)
                {
                    m_log.Info("[!]" + currentLine);
                }
            }
        }

        private void HandleClearAssets(string module, string[] args)
        {
            m_assetCache.Clear();
        }

        private void HandleForceUpdate(string module, string[] args)
        {
            m_console.Notice("Updating all clients");
            m_sceneManager.ForceCurrentSceneClientUpdate();
        }

        private void HandleEditScale(string module, string[] args)
        {
            if (args.Length == 6)
            {
                m_sceneManager.HandleEditCommandOnCurrentScene(args);
            }
            else
            {
                m_console.Notice("Argument error: edit scale <prim name> <x> <y> <z>");
            }
        }

        private void HandleCreateRegion(string module, string[] cmd)
        {
            if (cmd.Length < 4 || !cmd[3].EndsWith(".xml"))
            {
                m_console.Error("Usage: create region <region name> <region_file.xml>");
                return;
            }

            string regionsDir = ConfigSource.Source.Configs["Startup"].GetString("regionload_regionsdir", "Regions").Trim();
            string regionFile = String.Format("{0}/{1}", regionsDir, cmd[3]);
            // Allow absolute and relative specifiers
            if (cmd[3].StartsWith("/") || cmd[3].StartsWith("\\") || cmd[3].StartsWith(".."))
                regionFile = cmd[3];

            IScene scene;
            CreateRegion(new RegionInfo(cmd[2], regionFile, false, ConfigSource.Source), true, out scene);
        }

        private void HandleLoginEnable(string module, string[] cmd)
        {
            ProcessLogin(true);
        }

        private void HandleLoginDisable(string module, string[] cmd)
        {
            ProcessLogin(false);
        }

        private void HandleLoginStatus(string module, string[] cmd)
        {
            if (m_commsManager.GridService.RegionLoginsEnabled == false)

                m_log.Info("[ Login ]  Login are disabled ");
            else
                m_log.Info("[ Login ]  Login are enabled");
        }

        private void HandleConfig(string module, string[] cmd)
        {
            List<string> args = new List<string>(cmd);
            args.RemoveAt(0);
            string[] cmdparams = args.ToArray();
            string n = "CONFIG";

            if (cmdparams.Length > 0)
            {
                switch (cmdparams[0].ToLower())
                {
                    case "set":
                        if (cmdparams.Length < 4)
                        {
                            m_console.Error(n, "SYNTAX: " + n + " SET SECTION KEY VALUE");
                            m_console.Error(n, "EXAMPLE: " + n + " SET ScriptEngine.DotNetEngine NumberOfScriptThreads 5");
                        }
                        else
                        {
                            // IConfig c = DefaultConfig().Configs[cmdparams[1]];
                            // if (c == null)
                            //     c = DefaultConfig().AddConfig(cmdparams[1]);
                            IConfig c;
                            IConfigSource source = new IniConfigSource();
                            c = source.AddConfig(cmdparams[1]);
                            if (c != null)
                            {
                                string _value = String.Join(" ", cmdparams, 3, cmdparams.Length - 3);
                                c.Set(cmdparams[2], _value);
                                m_config.Source.Merge(source);

                                m_console.Error(n, n + " " + n + " " + cmdparams[1] + " " + cmdparams[2] + " " +
                                                   _value);
                            }
                        }
                        break;

                    case "get":
                        if (cmdparams.Length < 3)
                        {
                            m_console.Error(n, "SYNTAX: " + n + " GET SECTION KEY");
                            m_console.Error(n, "EXAMPLE: " + n + " GET ScriptEngine.DotNetEngine NumberOfScriptThreads");
                        }
                        else
                        {
                            IConfig c = m_config.Source.Configs[cmdparams[1]]; // DefaultConfig().Configs[cmdparams[1]];
                            if (c == null)
                            {
                                m_console.Notice(n, "Section \"" + cmdparams[1] + "\" does not exist.");
                                break;
                            }
                            else
                            {
                                m_console.Notice(n + " GET " + cmdparams[1] + " " + cmdparams[2] + ": " +
                                                 c.GetString(cmdparams[2]));
                            }
                        }

                        break;

                    case "save":
                        m_console.Notice("Saving configuration file: " + Application.iniFilePath);
                        m_config.Save(Application.iniFilePath);
                        break;
                }
            }
        }

        private void HandleModules(string module, string[] cmd)
        {
            List<string> args = new List<string>(cmd);
            args.RemoveAt(0);
            string[] cmdparams = args.ToArray();

            if (cmdparams.Length > 0)
            {
                switch (cmdparams[0].ToLower())
                {
                    case "list":
                        foreach (IRegionModule irm in m_moduleLoader.GetLoadedSharedModules)
                        {
                            m_console.Notice("Shared region module: " + irm.Name);
                        }
                        break;
                    case "unload":
                        if (cmdparams.Length > 1)
                        {
                            foreach (IRegionModule rm in new ArrayList(m_moduleLoader.GetLoadedSharedModules))
                            {
                                if (rm.Name.ToLower() == cmdparams[1].ToLower())
                                {
                                    m_console.Notice("Unloading module: " + rm.Name);
                                    m_moduleLoader.UnloadModule(rm);
                                }
                            }
                        }
                        break;
                    case "load":
                        if (cmdparams.Length > 1)
                        {
                            foreach (Scene s in new ArrayList(m_sceneManager.Scenes))
                            {
                                m_console.Notice("Loading module: " + cmdparams[1]);
                                m_moduleLoader.LoadRegionModules(cmdparams[1], s);
                            }
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Runs commands issued by the server console from the operator
        /// </summary>
        /// <param name="command">The first argument of the parameter (the command)</param>
        /// <param name="cmdparams">Additional arguments passed to the command</param>
        public void RunCommand(string module, string[] cmdparams)
        {
            List<string> args = new List<string>(cmdparams);
            if (args.Count < 1)
                return;

            string command = args[0];
            args.RemoveAt(0);

            cmdparams = args.ToArray();

            switch (command)
            {
                case "command-script":
                    if (cmdparams.Length > 0)
                    {
                        RunCommandScript(cmdparams[0]);
                    }
                    break;

                case "backup":
                    m_sceneManager.BackupCurrentScene();
                    break;

                case "remove-region":
                    string regRemoveName = CombineParams(cmdparams, 0);

                    Scene removeScene;
                    if (m_sceneManager.TryGetScene(regRemoveName, out removeScene))
                        RemoveRegion(removeScene, false);
                    else
                        m_console.Error("no region with that name");
                    break;

                case "delete-region":
                    string regDeleteName = CombineParams(cmdparams, 0);

                    Scene killScene;
                    if (m_sceneManager.TryGetScene(regDeleteName, out killScene))
                        RemoveRegion(killScene, true);
                    else
                        m_console.Error("no region with that name");
                    break;

                case "restart":
                    m_sceneManager.RestartCurrentScene();
                    break;

                case "Add-InventoryHost":
                    if (cmdparams.Length > 0)
                    {
                        m_commsManager.AddInventoryService(cmdparams[0]);
                    }
                    break;

                case "predecode-j2k":
                    if (cmdparams.Length > 0)
                    {
                        m_sceneManager.CacheJ2kDecode(Convert.ToInt32(cmdparams[0]));
                    }
                    else
                    {
                        m_sceneManager.CacheJ2kDecode(1);
                    }
                    break;

                case "link-region":
                case "unlink-region":
                case "link-mapping":
                    HGCommands.RunHGCommand(command, cmdparams, m_sceneManager.CurrentOrFirstScene);
                    break;
            }
        }

        /// <summary>
        /// Change the currently selected region.  The selected region is that operated upon by single region commands.
        /// </summary>
        /// <param name="cmdParams"></param>
        protected void ChangeSelectedRegion(string module, string[] cmdparams)
        {
            if (cmdparams.Length > 2)
            {
                string newRegionName = CombineParams(cmdparams, 2);

                if (!m_sceneManager.TrySetCurrentScene(newRegionName))
                    m_console.Error("Couldn't select region " + newRegionName);
            }
            else
            {
                m_console.Error("Usage: change region <region name>");
            }

            string regionName = (m_sceneManager.CurrentScene == null ? "root" : m_sceneManager.CurrentScene.RegionInfo.RegionName);
            m_console.Notice(String.Format("Currently selected region is {0}", regionName));
            m_console.DefaultPrompt = String.Format("Region ({0}) ", regionName);
            m_console.ConsoleScene = m_sceneManager.CurrentScene;
        }

        /// <summary>
        /// Execute switch for some of the create commands
        /// </summary>
        /// <param name="args"></param>
        private void HandleCreateUser(string module, string[] cmd)
        {
            if (ConfigurationSettings.Standalone)
            {
                CreateUser(cmd);
            }
            else
            {
                m_console.Notice("Create user is not available in grid mode, use the user server.");
            }
        }

        /// <summary>
        /// Execute switch for some of the reset commands
        /// </summary>
        /// <param name="args"></param>
        protected void HandleResetUserPassword(string module, string[] cmd)
        {
            if (ConfigurationSettings.Standalone)
            {
                ResetUserPassword(cmd);
            }
            else
            {
                m_console.Notice("Reset user password is not available in grid mode, use the user-server.");
            }
        }

        /// <summary>
        /// Turn on some debugging values for OpenSim.
        /// </summary>
        /// <param name="args"></param>
        protected void Debug(string module, string[] args)
        {
            if (args.Length == 1)
                return;

            switch (args[1])
            {
                case "packet":
                    if (args.Length > 2)
                    {
                        int newDebug;
                        if (int.TryParse(args[2], out newDebug))
                        {
                            m_sceneManager.SetDebugPacketLevelOnCurrentScene(newDebug);
                        }
                        else
                        {
                            m_console.Error("packet debug should be 0..255");
                        }
                        m_console.Notice("New packet debug: " + newDebug.ToString());
                    }

                    break;

                case "scene":
                    if (args.Length == 5)
                    {
                        if (m_sceneManager.CurrentScene == null)
                        {
                            m_console.Notice("Please use 'change region <regioname>' first");
                        }
                        else
                        {
                            bool scriptingOn = !Convert.ToBoolean(args[2]);
                            bool collisionsOn = !Convert.ToBoolean(args[3]);
                            bool physicsOn = !Convert.ToBoolean(args[4]);
                            m_sceneManager.CurrentScene.SetSceneCoreDebug(scriptingOn, collisionsOn, physicsOn);

                            m_console.Notice(
                                "CONSOLE",
                                String.Format(
                                    "Set debug scene scripting = {0}, collisions = {1}, physics = {2}",
                                    !scriptingOn, !collisionsOn, !physicsOn));
                        }
                    }
                    else
                    {
                        m_console.Error("debug scene <scripting> <collisions> <physics> (where inside <> is true/false)");
                    }

                    break;

                default:
                    m_console.Error("Unknown debug");
                    break;
            }
        }

        // see BaseOpenSimServer
        public override void HandleShow(string mod, string[] cmd)
        {
            base.HandleShow(mod, cmd);

            List<string> args = new List<string>(cmd);
            args.RemoveAt(0);
            string[] showParams = args.ToArray();

            switch (showParams[0])
            {
                case "assets":
                    m_assetCache.ShowState();
                    break;

                case "users":
                    IList agents;
                    if (showParams.Length > 1 && showParams[1] == "full")
                    {
                        agents = m_sceneManager.GetCurrentScenePresences();
                    }
                    else
                    {
                        agents = m_sceneManager.GetCurrentSceneAvatars();
                    }

                    m_console.Notice(String.Format("\nAgents connected: {0}\n", agents.Count));

                    m_console.Notice(
                        String.Format("{0,-16}{1,-16}{2,-37}{3,-11}{4,-16}", "Firstname", "Lastname",
                                      "Agent ID", "Root/Child", "Region"));

                    foreach (ScenePresence presence in agents)
                    {
                        RegionInfo regionInfo = m_sceneManager.GetRegionInfo(presence.RegionHandle);
                        string regionName;

                        if (regionInfo == null)
                        {
                            regionName = "Unresolvable";
                        }
                        else
                        {
                            regionName = regionInfo.RegionName;
                        }

                        m_console.Notice(
                            String.Format(
                                "{0,-16}{1,-16}{2,-37}{3,-11}{4,-16}",
                                presence.Firstname,
                                presence.Lastname,
                                presence.UUID,
                                presence.IsChildAgent ? "Child" : "Root",
                                regionName));
                    }

                    m_console.Notice("");
                    break;

                case "modules":
                    m_console.Notice("The currently loaded shared modules are:");
                    foreach (IRegionModule module in m_moduleLoader.GetLoadedSharedModules)
                    {
                        m_console.Notice("Shared Module: " + module.Name);
                    }
                    break;

                case "regions":
                    m_sceneManager.ForEachScene(
                        delegate(Scene scene)
                            {
                                m_console.Notice("Region Name: " + scene.RegionInfo.RegionName + " , Region XLoc: " +
                                                 scene.RegionInfo.RegionLocX + " , Region YLoc: " +
                                                 scene.RegionInfo.RegionLocY + " , Region Port: " +
                                                 scene.RegionInfo.InternalEndPoint.Port.ToString());
                            });
                    break;

                case "queues":
                    Notice(GetQueuesReport());
                    break;

                case "ratings":
                    m_sceneManager.ForEachScene(
                    delegate(Scene scene)
                    {
                        string rating = "";
                        if (scene.RegionInfo.RegionSettings.Maturity == 1)
                        {
                            rating = "MATURE";
                        }
                        else if (scene.RegionInfo.RegionSettings.Maturity == 2)
                        {
                            rating = "ADULT";
                        }
                        else
                        {
                            rating = "PG";
                        }
                        m_console.Notice("Region Name: " + scene.RegionInfo.RegionName + " , Region Rating: " +
                                         rating);
                    });
                    break;
            }
        }

        private string GetQueuesReport()
        {
            string report = String.Empty;

            m_sceneManager.ForEachScene(delegate(Scene scene)
                                            {
                                                scene.ForEachClient(delegate(IClientAPI client)
                                                                        {
                                                                            if (client is IStatsCollector)
                                                                            {
                                                                                report = report + client.FirstName +
                                                                                         " " + client.LastName + "\n";

                                                                                IStatsCollector stats =
                                                                                    (IStatsCollector) client;

                                                                                report = report + string.Format("{0,7} {1,7} {2,7} {3,7} {4,7} {5,7} {6,7} {7,7} {8,7} {9,7}\n",
                                                                                             "Send",
                                                                                             "In",
                                                                                             "Out",
                                                                                             "Resend",
                                                                                             "Land",
                                                                                             "Wind",
                                                                                             "Cloud",
                                                                                             "Task",
                                                                                             "Texture",
                                                                                             "Asset");
                                                                                report = report + stats.Report() +
                                                                                         "\n\n";
                                                                            }
                                                                        });
                                            });

            return report;
        }

        /// <summary>
        /// Create a new user
        /// </summary>
        /// <param name="cmdparams">string array with parameters: firstname, lastname, password, locationX, locationY, email</param>
        protected void CreateUser(string[] cmdparams)
        {
            string firstName;
            string lastName;
            string password;
            string email;
            uint regX = 1000;
            uint regY = 1000;

            if (cmdparams.Length < 3)
                firstName = MainConsole.Instance.CmdPrompt("First name", "Default");
            else firstName = cmdparams[2];

            if (cmdparams.Length < 4)
                lastName = MainConsole.Instance.CmdPrompt("Last name", "User");
            else lastName = cmdparams[3];

            if (cmdparams.Length < 5)
                password = MainConsole.Instance.PasswdPrompt("Password");
            else password = cmdparams[4];

            if (cmdparams.Length < 6)
                regX = Convert.ToUInt32(MainConsole.Instance.CmdPrompt("Start Region X", regX.ToString()));
            else regX = Convert.ToUInt32(cmdparams[5]);

            if (cmdparams.Length < 7)
                regY = Convert.ToUInt32(MainConsole.Instance.CmdPrompt("Start Region Y", regY.ToString()));
            else regY = Convert.ToUInt32(cmdparams[6]);

            if (cmdparams.Length < 8)
                email = MainConsole.Instance.CmdPrompt("Email", "");
            else email = cmdparams[7];

            if (null == m_commsManager.UserProfileCacheService.GetUserDetails(firstName, lastName))
            {
                m_commsManager.UserAdminService.AddUser(firstName, lastName, password, email, regX, regY);
            }
            else
            {
                m_log.ErrorFormat("[CONSOLE]: A user with the name {0} {1} already exists!", firstName, lastName);
            }
        }

        /// <summary>
        /// Reset a user password.
        /// </summary>
        /// <param name="cmdparams"></param>
        private void ResetUserPassword(string[] cmdparams)
        {
            string firstName;
            string lastName;
            string newPassword;

            if (cmdparams.Length < 4)
                firstName = MainConsole.Instance.CmdPrompt("First name");
            else firstName = cmdparams[3];

            if (cmdparams.Length < 5)
                lastName = MainConsole.Instance.CmdPrompt("Last name");
            else lastName = cmdparams[4];

            if (cmdparams.Length < 6)
                newPassword = MainConsole.Instance.PasswdPrompt("New password");
            else newPassword = cmdparams[5];

            m_commsManager.UserAdminService.ResetUserPassword(firstName, lastName, newPassword);
        }

        protected void SavePrimsXml2(string module, string[] cmdparams)
        {
            if (cmdparams.Length > 5)
            {
                m_sceneManager.SaveNamedPrimsToXml2(cmdparams[3], cmdparams[4]);
            }
            else
            {
                m_sceneManager.SaveNamedPrimsToXml2("Primitive", DEFAULT_PRIM_BACKUP_FILENAME);
            }
        }

        protected void SaveXml(string module, string[] cmdparams)
        {
            m_log.Error("[CONSOLE]: PLEASE NOTE, save-xml is DEPRECATED and may be REMOVED soon.  If you are using this and there is some reason you can't use save-xml2, please file a mantis detailing the reason.");

            if (cmdparams.Length > 0)
            {
                m_sceneManager.SaveCurrentSceneToXml(cmdparams[2]);
            }
            else
            {
                m_sceneManager.SaveCurrentSceneToXml(DEFAULT_PRIM_BACKUP_FILENAME);
            }
        }

        protected void LoadXml(string module, string[] cmdparams)
        {
            m_log.Error("[CONSOLE]: PLEASE NOTE, load-xml is DEPRECATED and may be REMOVED soon.  If you are using this and there is some reason you can't use load-xml2, please file a mantis detailing the reason.");

            Vector3 loadOffset = new Vector3(0, 0, 0);
            if (cmdparams.Length > 2)
            {
                bool generateNewIDS = false;
                if (cmdparams.Length > 3)
                {
                    if (cmdparams[3] == "-newUID")
                    {
                        generateNewIDS = true;
                    }
                    if (cmdparams.Length > 4)
                    {
                        loadOffset.X = (float) Convert.ToDecimal(cmdparams[4]);
                        if (cmdparams.Length > 5)
                        {
                            loadOffset.Y = (float) Convert.ToDecimal(cmdparams[5]);
                        }
                        if (cmdparams.Length > 6)
                        {
                            loadOffset.Z = (float) Convert.ToDecimal(cmdparams[6]);
                        }
                        m_console.Error("loadOffsets <X,Y,Z> = <" + loadOffset.X + "," + loadOffset.Y + "," +
                                        loadOffset.Z + ">");
                    }
                }
                m_sceneManager.LoadCurrentSceneFromXml(cmdparams[0], generateNewIDS, loadOffset);
            }
            else
            {
                try
                {
                    m_sceneManager.LoadCurrentSceneFromXml(DEFAULT_PRIM_BACKUP_FILENAME, false, loadOffset);
                }
                catch (FileNotFoundException)
                {
                    m_console.Error("Default xml not found. Usage: load-xml <filename>");
                }
            }
        }

        protected void SaveXml2(string module, string[] cmdparams)
        {
            if (cmdparams.Length > 2)
            {
                m_sceneManager.SaveCurrentSceneToXml2(cmdparams[2]);
            }
            else
            {
                m_sceneManager.SaveCurrentSceneToXml2(DEFAULT_PRIM_BACKUP_FILENAME);
            }
        }

        protected void LoadXml2(string module, string[] cmdparams)
        {
            if (cmdparams.Length > 2)
            {
                try
                {
                    m_sceneManager.LoadCurrentSceneFromXml2(cmdparams[2]);
                }
                catch (FileNotFoundException)
                {
                    m_console.Error("Specified xml not found. Usage: load xml2 <filename>");
                }
            }
            else
            {
                try
                {
                    m_sceneManager.LoadCurrentSceneFromXml2(DEFAULT_PRIM_BACKUP_FILENAME);
                }
                catch (FileNotFoundException)
                {
                    m_console.Error("Default xml not found. Usage: load xml2 <filename>");
                }
            }
        }

        /// <summary>
        /// Load a whole region from an opensim archive.
        /// </summary>
        /// <param name="cmdparams"></param>
        protected void LoadOar(string module, string[] cmdparams)
        {
            if (cmdparams.Length > 2)
            {
                try
                {
                    m_sceneManager.LoadArchiveToCurrentScene(cmdparams[2]);
                }
                catch (FileNotFoundException)
                {
                    m_console.Error("Specified oar not found. Usage: load oar <filename>");
                }
            }
            else
            {
                try
                {
                    m_sceneManager.LoadArchiveToCurrentScene(DEFAULT_OAR_BACKUP_FILENAME);
                }
                catch (FileNotFoundException)
                {
                    m_console.Error("Default oar not found. Usage: load oar <filename>");
                }
            }
        }

        /// <summary>
        /// Save a region to a file, including all the assets needed to restore it.
        /// </summary>
        /// <param name="cmdparams"></param>
        protected void SaveOar(string module, string[] cmdparams)
        {
            if (cmdparams.Length > 2)
            {
                m_sceneManager.SaveCurrentSceneToArchive(cmdparams[2]);
            }
            else
            {
                m_sceneManager.SaveCurrentSceneToArchive(DEFAULT_OAR_BACKUP_FILENAME);
            }
        }

        private static string CombineParams(string[] commandParams, int pos)
        {
            string result = String.Empty;
            for (int i = pos; i < commandParams.Length; i++)
            {
                result += commandParams[i] + " ";
            }
            result = result.TrimEnd(' ');
            return result;
        }

        #endregion
    }
}
