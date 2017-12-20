/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyrightD
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
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Timers;
using System.Xml;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.Imaging;
using OpenSim.Framework;
using OpenSim.Services.Interfaces;
using OpenSim.Framework.Communications;
using OpenSim.Framework.Communications.Cache;
using OpenSim.Framework.Communications.Clients;
using OpenSim.Framework.Console;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes.Scripting;
using OpenSim.Region.Framework.Scenes.Serialization;
using OpenSim.Region.Physics.Manager;
using Timer=System.Timers.Timer;
using TPFlags = OpenSim.Framework.Constants.TeleportFlags;

namespace OpenSim.Region.Framework.Scenes
{
    public delegate bool FilterAvatarList(ScenePresence avatar);

    public partial class Scene : SceneBase
    {
        public delegate void SynchronizeSceneHandler(Scene scene);
        public SynchronizeSceneHandler SynchronizeScene = null;

        /* Used by the loadbalancer plugin on GForge */
        protected int m_splitRegionID = 0;
        public int SplitRegionID
        {
            get { return m_splitRegionID; }
            set { m_splitRegionID = value; }
        }

        private const long DEFAULT_MIN_TIME_FOR_PERSISTENCE = 60L;
        private const long DEFAULT_MAX_TIME_FOR_PERSISTENCE = 600L;

        #region Fields

        protected Timer m_restartWaitTimer = new Timer();

        protected Thread m_updateEntitiesThread;

        public SimStatsReporter StatsReporter;

        protected List<RegionInfo> m_regionRestartNotifyList = new List<RegionInfo>();
        protected List<RegionInfo> m_neighbours = new List<RegionInfo>();

        /// <value>
        /// The scene graph for this scene
        /// </value>
        /// TODO: Possibly stop other classes being able to manipulate this directly.
        private SceneGraph m_sceneGraph;

        /// <summary>
        /// Are we applying physics to any of the prims in this scene?
        /// </summary>
        public bool m_physicalPrim;
        public float m_maxNonphys = 256;
        public float m_maxPhys = 10;
        public bool m_clampPrimSize = false;
        public bool m_trustBinaries = false;
        public bool m_allowScriptCrossings = false;
        public bool m_useFlySlow = false;
        public bool m_usePreJump = false;
        public bool m_seeIntoRegionFromNeighbor;
        // TODO: need to figure out how allow client agents but deny
        // root agents when ACL denies access to root agent
        public bool m_strictAccessControl = true;
        public int MaxUndoCount = 5;
        private int m_RestartTimerCounter;
        private readonly Timer m_restartTimer = new Timer(15000); // Wait before firing
        private int m_incrementsof15seconds = 0;
        private volatile bool m_backingup = false;

        private Dictionary<UUID, ReturnInfo> m_returns = new Dictionary<UUID, ReturnInfo>();

        protected string m_simulatorVersion = "OpenSimulator Server";

        protected ModuleLoader m_moduleLoader;
        protected StorageManager m_storageManager;
        protected AgentCircuitManager m_authenticateHandler;
        public CommunicationsManager CommsManager;

        protected SceneCommunicationService m_sceneGridService;

        public SceneCommunicationService SceneGridService
        {
            get { return m_sceneGridService; }
        }

        public IXfer XferManager;

        protected IAssetService m_AssetService = null;

        public IAssetService AssetService
        {
            get
            {
                if (m_AssetService == null)
                    m_AssetService = RequestModuleInterface<IAssetService>();

                return m_AssetService;
            }
        }

        protected IXMLRPC m_xmlrpcModule;
        protected IWorldComm m_worldCommModule;
        protected IAvatarFactory m_AvatarFactory;
        protected IConfigSource m_config;
        protected IRegionSerializerModule m_serializer;
        protected IInterregionCommsOut m_interregionCommsOut;
        protected IInterregionCommsIn m_interregionCommsIn;
        protected IDialogModule m_dialogModule;

        protected ICapabilitiesModule m_capsModule;
        public ICapabilitiesModule CapsModule
        {
            get { return m_capsModule; }
        }
        
        protected override IConfigSource GetConfig()
        {
            return m_config;
        }

        // Central Update Loop

        protected int m_fps = 10;
        protected int m_frame = 0;
        protected float m_timespan = 0.089f;
        protected DateTime m_lastupdate = DateTime.Now;

        private int m_update_physics = 1;
        private int m_update_entitymovement = 1;
        private int m_update_entities = 1; // Run through all objects checking for updates
        private int m_update_entitiesquick = 200; // Run through objects that have scheduled updates checking for updates
        private int m_update_presences = 1; // Update scene presence movements
        private int m_update_events = 1;
        private int m_update_backup = 200;
        private int m_update_terrain = 50;
        private int m_update_land = 1;

        private int frameMS = 0;
        private int physicsMS2 = 0;
        private int physicsMS = 0;
        private int otherMS = 0;

        private bool m_physics_enabled = true;
        private bool m_scripts_enabled = true;
        private string m_defaultScriptEngine;
        private int m_LastLogin = 0;
        private Thread HeartbeatThread;
        private volatile bool shuttingdown = false;

        private int m_lastUpdate = Environment.TickCount;
        private int m_maxPrimsPerFrame = 200;

        private object m_deleting_scene_object = new object();

        // the minimum time that must elapse before a changed object will be considered for persisted
        public long m_dontPersistBefore = DEFAULT_MIN_TIME_FOR_PERSISTENCE * 10000000L;
        // the maximum time that must elapse before a changed object will be considered for persisted
        public long m_persistAfter = DEFAULT_MAX_TIME_FOR_PERSISTENCE * 10000000L;

        #endregion

        #region Properties

        public AgentCircuitManager AuthenticateHandler
        {
            get { return m_authenticateHandler; }
        }

        public SceneGraph SceneContents
        {
            get { return m_sceneGraph; }
        }

        // an instance to the physics plugin's Scene object.
        public PhysicsScene PhysicsScene
        {
            get { return m_sceneGraph.PhysicsScene; }
            set
            {
                // If we're not doing the initial set
                // Then we've got to remove the previous
                // event handler
                if (PhysicsScene != null && PhysicsScene.SupportsNINJAJoints)
                {
                    PhysicsScene.OnJointMoved -= jointMoved;
                    PhysicsScene.OnJointDeactivated -= jointDeactivated;
                    PhysicsScene.OnJointErrorMessage -= jointErrorMessage;
                }

                m_sceneGraph.PhysicsScene = value;

                if (PhysicsScene != null && m_sceneGraph.PhysicsScene.SupportsNINJAJoints)
                {
                    // register event handlers to respond to joint movement/deactivation
                    PhysicsScene.OnJointMoved += jointMoved;
                    PhysicsScene.OnJointDeactivated += jointDeactivated;
                    PhysicsScene.OnJointErrorMessage += jointErrorMessage;
                }
            }
        }

        // This gets locked so things stay thread safe.
        public object SyncRoot
        {
            get { return m_sceneGraph.m_syncRoot; }
        }

        public int MaxPrimsPerFrame
        {
            get { return m_maxPrimsPerFrame; }
            set { m_maxPrimsPerFrame = value; }
        }

        /// <summary>
        /// This is for llGetRegionFPS
        /// </summary>
        public float SimulatorFPS
        {
            get { return StatsReporter.getLastReportedSimFPS(); }
        }

        public string DefaultScriptEngine
        {
            get { return m_defaultScriptEngine; }
        }

        // Reference to all of the agents in the scene (root and child)
        protected Dictionary<UUID, ScenePresence> m_scenePresences
        {
            get { return m_sceneGraph.ScenePresences; }
            set { m_sceneGraph.ScenePresences = value; }
        }

        public EntityManager Entities
        {
            get { return m_sceneGraph.Entities; }
        }

        public Dictionary<UUID, ScenePresence> m_restorePresences
        {
            get { return m_sceneGraph.RestorePresences; }
            set { m_sceneGraph.RestorePresences = value; }
        }

        public int objectCapacity = 45000;

        #endregion

        #region Constructors

        public Scene(RegionInfo regInfo, AgentCircuitManager authen,
                     CommunicationsManager commsMan, SceneCommunicationService sceneGridService,
                     StorageManager storeManager,
                     ModuleLoader moduleLoader, bool dumpAssetsToFile, bool physicalPrim,
                     bool SeeIntoRegionFromNeighbor, IConfigSource config, string simulatorVersion)
        {
            m_config = config;

            Random random = new Random();
            m_lastAllocatedLocalId = (uint)(random.NextDouble() * (double)(uint.MaxValue/2))+(uint)(uint.MaxValue/4);
            m_moduleLoader = moduleLoader;
            m_authenticateHandler = authen;
            CommsManager = commsMan;
            m_sceneGridService = sceneGridService;
            m_storageManager = storeManager;
            m_regInfo = regInfo;
            m_regionHandle = m_regInfo.RegionHandle;
            m_regionName = m_regInfo.RegionName;
            m_datastore = m_regInfo.DataStore;

            m_physicalPrim = physicalPrim;
            m_seeIntoRegionFromNeighbor = SeeIntoRegionFromNeighbor;

            m_eventManager = new EventManager();
            m_permissions = new ScenePermissions(this);

            m_asyncSceneObjectDeleter = new AsyncSceneObjectGroupDeleter(this);
            m_asyncSceneObjectDeleter.Enabled = true;

            // Load region settings
            m_regInfo.RegionSettings = m_storageManager.DataStore.LoadRegionSettings(m_regInfo.RegionID);
            if (m_storageManager.EstateDataStore != null)
                m_regInfo.EstateSettings = m_storageManager.EstateDataStore.LoadEstateSettings(m_regInfo.RegionID);

            //Bind Storage Manager functions to some land manager functions for this scene
            EventManager.OnLandObjectAdded +=
                new EventManager.LandObjectAdded(m_storageManager.DataStore.StoreLandObject);
            EventManager.OnLandObjectRemoved +=
                new EventManager.LandObjectRemoved(m_storageManager.DataStore.RemoveLandObject);

            m_sceneGraph = new SceneGraph(this, m_regInfo);

            // If the scene graph has an Unrecoverable error, restart this sim.
            // Currently the only thing that causes it to happen is two kinds of specific
            // Physics based crashes.
            //
            // Out of memory
            // Operating system has killed the plugin
            m_sceneGraph.UnRecoverableError += RestartNow;

            RegisterDefaultSceneEvents();

            DumpAssetsToFile = dumpAssetsToFile;

            m_scripts_enabled = !RegionInfo.RegionSettings.DisableScripts;

            m_physics_enabled = !RegionInfo.RegionSettings.DisablePhysics;

            StatsReporter = new SimStatsReporter(this);
            StatsReporter.OnSendStatsResult += SendSimStatsPackets;
            StatsReporter.OnStatsIncorrect += m_sceneGraph.RecalculateStats;

            StatsReporter.SetObjectCapacity(objectCapacity);

            m_simulatorVersion = simulatorVersion
                + " (OS " + Util.GetOperatingSystemInformation() + ")"
                + " ChilTasks:" + m_seeIntoRegionFromNeighbor.ToString()
                + " PhysPrim:" + m_physicalPrim.ToString();

            try
            {
                // Region config overrides global config
                //
                IConfig startupConfig = m_config.Configs["Startup"];

                //Animation states
                m_useFlySlow = startupConfig.GetBoolean("enableflyslow", false);
                // TODO: Change default to true once the feature is supported
                m_usePreJump = startupConfig.GetBoolean("enableprejump", false);

                m_maxNonphys = startupConfig.GetFloat("NonPhysicalPrimMax", m_maxNonphys);
                if (RegionInfo.NonphysPrimMax > 0)
                    m_maxNonphys = RegionInfo.NonphysPrimMax;

                m_maxPhys = startupConfig.GetFloat("PhysicalPrimMax", m_maxPhys);

                if (RegionInfo.PhysPrimMax > 0)
                    m_maxPhys = RegionInfo.PhysPrimMax;

                // Here, if clamping is requested in either global or
                // local config, it will be used
                //
                m_clampPrimSize = startupConfig.GetBoolean("ClampPrimSize", m_clampPrimSize);
                if (RegionInfo.ClampPrimSize)
                    m_clampPrimSize = true;

                m_trustBinaries = startupConfig.GetBoolean("TrustBinaries", m_trustBinaries);
                m_allowScriptCrossings = startupConfig.GetBoolean("AllowScriptCrossing", m_allowScriptCrossings);
                m_dontPersistBefore =
                  startupConfig.GetLong("MinimumTimeBeforePersistenceConsidered", DEFAULT_MIN_TIME_FOR_PERSISTENCE);
                m_dontPersistBefore *= 10000000;
                m_persistAfter =
                  startupConfig.GetLong("MaximumTimeBeforePersistenceConsidered", DEFAULT_MAX_TIME_FOR_PERSISTENCE);
                m_persistAfter *= 10000000;

                m_defaultScriptEngine = startupConfig.GetString("DefaultScriptEngine", "DotNetEngine");

                m_maxPrimsPerFrame = startupConfig.GetInt("MaxPrimsPerFrame", 200);
                IConfig packetConfig = m_config.Configs["PacketPool"];
                if (packetConfig != null)
                {
                    PacketPool.Instance.RecyclePackets = packetConfig.GetBoolean("RecyclePackets", true);
                    PacketPool.Instance.RecycleDataBlocks = packetConfig.GetBoolean("RecycleDataBlocks", true);
                }

                m_strictAccessControl = startupConfig.GetBoolean("StrictAccessControl", m_strictAccessControl);
            }
            catch
            {
                m_log.Warn("[SCENE]: Failed to load StartupConfig");
            }
        }

        /// <summary>
        /// Mock constructor for scene group persistency unit tests.
        /// SceneObjectGroup RegionId property is delegated to Scene.
        /// </summary>
        /// <param name="regInfo"></param>
        public Scene(RegionInfo regInfo)
        {
            m_regInfo = regInfo;
            m_eventManager = new EventManager();
        }

        #endregion

        #region Startup / Close Methods

        public bool ShuttingDown
        {
            get { return shuttingdown; }
        }

        /// <value>
        /// The scene graph for this scene
        /// </value>
        /// TODO: Possibly stop other classes being able to manipulate this directly.
        public SceneGraph SceneGraph
        {
            get { return m_sceneGraph; }
        }

        protected virtual void RegisterDefaultSceneEvents()
        {
            IDialogModule dm = RequestModuleInterface<IDialogModule>();

            if (dm != null)
                m_eventManager.OnPermissionError += dm.SendAlertToUser;
        }

        public override string GetSimulatorVersion()
        {
            return m_simulatorVersion;
        }

        /// <summary>
        /// Another region is up. Gets called from Grid Comms:
        /// (OGS1 -> LocalBackEnd -> RegionListened -> SceneCommunicationService)
        /// We have to tell all our ScenePresences about it, and add it to the
        /// neighbor list.
        ///
        /// We only add it to the neighbor list if it's within 1 region from here.
        /// Agents may have draw distance values that cross two regions though, so
        /// we add it to the notify list regardless of distance. We'll check
        /// the agent's draw distance before notifying them though.
        /// </summary>
        /// <param name="otherRegion">RegionInfo handle for the new region.</param>
        /// <returns>True after all operations complete, throws exceptions otherwise.</returns>
        public override bool OtherRegionUp(RegionInfo otherRegion)
        {
            m_log.InfoFormat("[SCENE]: Region {0} up in coords {1}-{2}", otherRegion.RegionName, otherRegion.RegionLocX, otherRegion.RegionLocY);

            if (RegionInfo.RegionHandle != otherRegion.RegionHandle)
            {
                for (int i = 0; i < m_neighbours.Count; i++)
                {
                    // The purpose of this loop is to re-update the known neighbors
                    // when another region comes up on top of another one.
                    // The latest region in that location ends up in the
                    // 'known neighbors list'
                    // Additionally, the commFailTF property gets reset to false.
                    if (m_neighbours[i].RegionHandle == otherRegion.RegionHandle)
                    {
                        lock (m_neighbours)
                        {
                            m_neighbours[i] = otherRegion;

                        }
                    }
                }

                // If the value isn't in the neighbours, add it.
                // If the RegionInfo isn't exact but is for the same XY World location,
                // then the above loop will fix that.

                if (!(CheckNeighborRegion(otherRegion)))
                {
                    lock (m_neighbours)
                    {
                        m_neighbours.Add(otherRegion);
                        //m_log.Info("[UP]: " + otherRegion.RegionHandle.ToString());
                    }
                }

                // If these are cast to INT because long + negative values + abs returns invalid data
                int resultX = Math.Abs((int)otherRegion.RegionLocX - (int)RegionInfo.RegionLocX);
                int resultY = Math.Abs((int)otherRegion.RegionLocY - (int)RegionInfo.RegionLocY);
                if (resultX <= 1 && resultY <= 1)
                {
                    try
                    {
                        ForEachScenePresence(delegate(ScenePresence agent)
                                             {
                                                 // If agent is a root agent.
                                                 if (!agent.IsChildAgent)
                                                 {
                                                     //agent.ControllingClient.new
                                                     //this.CommsManager.InterRegion.InformRegionOfChildAgent(otherRegion.RegionHandle, agent.ControllingClient.RequestClientInfo());

                                                     List<ulong> old = new List<ulong>();
                                                     old.Add(otherRegion.RegionHandle);
                                                     agent.DropOldNeighbours(old);
                                                     InformClientOfNeighbor(agent, otherRegion);
                                                 }
                                             }
                            );
                    }
                    catch (NullReferenceException)
                    {
                        // This means that we're not booted up completely yet.
                        // This shouldn't happen too often anymore.
                        m_log.Error("[SCENE]: Couldn't inform client of regionup because we got a null reference exception");
                    }
                }
                else
                {
                    m_log.Info("[INTERGRID]: Got notice about far away Region: " + otherRegion.RegionName.ToString() +
                               " at  (" + otherRegion.RegionLocX.ToString() + ", " +
                               otherRegion.RegionLocY.ToString() + ")");
                }
            }
            return true;
        }

        public void AddNeighborRegion(RegionInfo region)
        {
            lock (m_neighbours)
            {
                if (!CheckNeighborRegion(region))
                {
                    m_neighbours.Add(region);
                }
            }
        }

        public bool CheckNeighborRegion(RegionInfo region)
        {
            bool found = false;
            lock (m_neighbours)
            {
                foreach (RegionInfo reg in m_neighbours)
                {
                    if (reg.RegionHandle == region.RegionHandle)
                    {
                        found = true;
                        break;
                    }
                }
            }
            return found;
        }

        // Alias IncomingHelloNeighbour OtherRegionUp, for now
        public bool IncomingHelloNeighbour(RegionInfo neighbour)
        {
            return OtherRegionUp(neighbour);
        }

        /// <summary>
        /// Given float seconds, this will restart the region.
        /// </summary>
        /// <param name="seconds">float indicating duration before restart.</param>
        public virtual void Restart(float seconds)
        {
            // notifications are done in 15 second increments
            // so ..   if the number of seconds is less then 15 seconds, it's not really a restart request
            // It's a 'Cancel restart' request.

            // RestartNow() does immediate restarting.
            if (seconds < 15)
            {
                m_restartTimer.Stop();
                m_dialogModule.SendGeneralAlert("Restart Aborted");
            }
            else
            {
                // Now we figure out what to set the timer to that does the notifications and calls, RestartNow()
                m_restartTimer.Interval = 15000;
                m_incrementsof15seconds = (int)seconds / 15;
                m_RestartTimerCounter = 0;
                m_restartTimer.AutoReset = true;
                m_restartTimer.Elapsed += new ElapsedEventHandler(RestartTimer_Elapsed);
                m_log.Info("[REGION]: Restarting Region in " + (seconds / 60) + " minutes");
                m_restartTimer.Start();
                m_dialogModule.SendNotificationToUsersInRegion(
                    UUID.Random(), String.Empty, RegionInfo.RegionName + ": Restarting in 2 Minutes");
            }
        }

        // The Restart timer has occured.
        // We have to figure out if this is a notification or if the number of seconds specified in Restart
        // have elapsed.
        // If they have elapsed, call RestartNow()
        public void RestartTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            m_RestartTimerCounter++;
            if (m_RestartTimerCounter <= m_incrementsof15seconds)
            {
                if (m_RestartTimerCounter == 4 || m_RestartTimerCounter == 6 || m_RestartTimerCounter == 7)
                    m_dialogModule.SendNotificationToUsersInRegion(
                        UUID.Random(),
                        String.Empty,
                        RegionInfo.RegionName + ": Restarting in " + ((8 - m_RestartTimerCounter) * 15) + " seconds");
            }
            else
            {
                m_restartTimer.Stop();
                m_restartTimer.AutoReset = false;
                RestartNow();
            }
        }

        // This causes the region to restart immediatley.
        public void RestartNow()
        {
            if (PhysicsScene != null)
            {
                PhysicsScene.Dispose();
            }

            m_log.Error("[REGION]: Closing");
            Close();
            m_log.Error("[REGION]: Firing Region Restart Message");
            base.Restart(0);
        }

        // This is a helper function that notifies root agents in this region that a new sim near them has come up
        // This is in the form of a timer because when an instance of OpenSim.exe is started,
        // Even though the sims initialize, they don't listen until 'all of the sims are initialized'
        // If we tell an agent about a sim that's not listening yet, the agent will not be able to connect to it.
        // subsequently the agent will never see the region come back online.
        public void RestartNotifyWaitElapsed(object sender, ElapsedEventArgs e)
        {
            m_restartWaitTimer.Stop();
            lock (m_regionRestartNotifyList)
            {
                foreach (RegionInfo region in m_regionRestartNotifyList)
                {
                    try
                    {
                        ForEachScenePresence(delegate(ScenePresence agent)
                                             {
                                                 // If agent is a root agent.
                                                 if (!agent.IsChildAgent)
                                                 {
                                                     //agent.ControllingClient.new
                                                     //this.CommsManager.InterRegion.InformRegionOfChildAgent(otherRegion.RegionHandle, agent.ControllingClient.RequestClientInfo());
                                                     InformClientOfNeighbor(agent, region);
                                                 }
                                             }
                            );
                    }
                    catch (NullReferenceException)
                    {
                        // This means that we're not booted up completely yet.
                        // This shouldn't happen too often anymore.
                    }
                }

                // Reset list to nothing.
                m_regionRestartNotifyList.Clear();
            }
        }

        public void SetSceneCoreDebug(bool ScriptEngine, bool CollisionEvents, bool PhysicsEngine)
        {
            if (m_scripts_enabled != !ScriptEngine)
            {
                // Tedd!   Here's the method to disable the scripting engine!
                if (ScriptEngine)
                {
                    m_log.Info("Stopping all Scripts in Scene");
                    foreach (EntityBase ent in Entities)
                    {
                        if (ent is SceneObjectGroup)
                        {
                            ((SceneObjectGroup) ent).RemoveScriptInstances();
                        }
                    }
                }
                else
                {
                    m_log.Info("Starting all Scripts in Scene");
                    lock (Entities)
                    {
                        foreach (EntityBase ent in Entities)
                        {
                            if (ent is SceneObjectGroup)
                            {
                                ((SceneObjectGroup)ent).CreateScriptInstances(0, false, DefaultScriptEngine, 0);
                            }
                        }
                    }
                }
                m_scripts_enabled = !ScriptEngine;
                m_log.Info("[TOTEDD]: Here is the method to trigger disabling of the scripting engine");
            }

            if (m_physics_enabled != !PhysicsEngine)
            {
                m_physics_enabled = !PhysicsEngine;
            }
        }

        public int GetInaccurateNeighborCount()
        {
            lock (m_neighbours)
            {
                return m_neighbours.Count;
            }
        }

        // This is the method that shuts down the scene.
        public override void Close()
        {
            m_log.InfoFormat("[SCENE]: Closing down the single simulator: {0}", RegionInfo.RegionName);

            // Kick all ROOT agents with the message, 'The simulator is going down'
            ForEachScenePresence(delegate(ScenePresence avatar)
                                 {
                                     if (avatar.KnownChildRegionHandles.Contains(RegionInfo.RegionHandle))
                                         avatar.KnownChildRegionHandles.Remove(RegionInfo.RegionHandle);

                                     if (!avatar.IsChildAgent)
                                         avatar.ControllingClient.Kick("The simulator is going down.");

                                     avatar.ControllingClient.SendShutdownConnectionNotice();
                                 });

            // Wait here, or the kick messages won't actually get to the agents before the scene terminates.
            Thread.Sleep(500);

            // Stop all client threads.
            ForEachScenePresence(delegate(ScenePresence avatar) { avatar.ControllingClient.Close(true); });

            // Stop updating the scene objects and agents.
            //m_heartbeatTimer.Close();
            shuttingdown = true;

            m_log.Debug("[SCENE]: Persisting changed objects");
            List<EntityBase> entities = GetEntities();
            foreach (EntityBase entity in entities)
            {
                if (!entity.IsDeleted && entity is SceneObjectGroup && ((SceneObjectGroup)entity).HasGroupChanged)
                {
                    ((SceneObjectGroup)entity).ProcessBackup(m_storageManager.DataStore, false);
                }
            }

            m_sceneGraph.Close();

            // De-register with region communications (events cleanup)
            UnRegisterRegionWithComms();

            // call the base class Close method.
            base.Close();
        }

        /// <summary>
        /// Start the timer which triggers regular scene updates
        /// </summary>
        public void StartTimer()
        {
            //m_log.Debug("[SCENE]: Starting timer");
            //m_heartbeatTimer.Enabled = true;
            //m_heartbeatTimer.Interval = (int)(m_timespan * 1000);
            //m_heartbeatTimer.Elapsed += new ElapsedEventHandler(Heartbeat);
            HeartbeatThread = new Thread(new ParameterizedThreadStart(Heartbeat));
            HeartbeatThread.SetApartmentState(ApartmentState.MTA);
            HeartbeatThread.Name = string.Format("Heartbeat for region {0}", RegionInfo.RegionName);
            HeartbeatThread.Priority = ThreadPriority.AboveNormal;
            ThreadTracker.Add(HeartbeatThread);
            HeartbeatThread.Start();
        }

        /// <summary>
        /// Sets up references to modules required by the scene
        /// </summary>
        public void SetModuleInterfaces()
        {
            m_xmlrpcModule = RequestModuleInterface<IXMLRPC>();
            m_worldCommModule = RequestModuleInterface<IWorldComm>();
            XferManager = RequestModuleInterface<IXfer>();
            m_AvatarFactory = RequestModuleInterface<IAvatarFactory>();
            m_serializer = RequestModuleInterface<IRegionSerializerModule>();
            m_interregionCommsOut = RequestModuleInterface<IInterregionCommsOut>();
            m_interregionCommsIn = RequestModuleInterface<IInterregionCommsIn>();
            m_dialogModule = RequestModuleInterface<IDialogModule>();
            m_capsModule = RequestModuleInterface<ICapabilitiesModule>();
        }

        #endregion

        #region Update Methods

        /// <summary>
        /// Performs per-frame updates regularly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Heartbeat(object sender)
        {
            Update();

            m_lastUpdate = Environment.TickCount;
        }

        /// <summary>
        /// Performs per-frame updates on the scene, this should be the central scene loop
        /// </summary>
        public override void Update()
        {
            int maintc = 0;
            while (!shuttingdown)
            {
                maintc = Environment.TickCount;

                TimeSpan SinceLastFrame = DateTime.Now - m_lastupdate;
                // Aquire a lock so only one update call happens at once
                //updateLock.WaitOne();
                float physicsFPS = 0;
                //m_log.Info("sadfadf" + m_neighbours.Count.ToString());
                int agentsInScene = m_sceneGraph.GetRootAgentCount() + m_sceneGraph.GetChildAgentCount();

                if (agentsInScene > 21)
                {
                    if (m_update_entities == 1)
                    {
                        m_update_entities = 5;
                        StatsReporter.SetUpdateMS(6000);
                    }
                }
                else
                {
                    if (m_update_entities == 5)
                    {
                        m_update_entities = 1;
                        StatsReporter.SetUpdateMS(3000);
                    }
                }

                frameMS = Environment.TickCount;
                try
                {
                    // Increment the frame counter
                    m_frame++;

                    // Loop it
                    if (m_frame == Int32.MaxValue)
                        m_frame = 0;

                    physicsMS2 = Environment.TickCount;
                    if ((m_frame % m_update_physics == 0) && m_physics_enabled)
                        m_sceneGraph.UpdatePreparePhysics();
                    physicsMS2 = Environment.TickCount - physicsMS2;

                    if (m_frame % m_update_entitymovement == 0)
                        m_sceneGraph.UpdateEntityMovement();

                    physicsMS = Environment.TickCount;
                    if ((m_frame % m_update_physics == 0) && m_physics_enabled)
                        physicsFPS = m_sceneGraph.UpdatePhysics(
                            Math.Max(SinceLastFrame.TotalSeconds, m_timespan)
                            );
                    if (m_frame % m_update_physics == 0 && SynchronizeScene != null)
                        SynchronizeScene(this);

                    physicsMS = Environment.TickCount - physicsMS;
                    physicsMS += physicsMS2;

                    otherMS = Environment.TickCount;
                    // run through all entities looking for updates (slow)
                    if (m_frame % m_update_entities == 0)
                    {
                        /* // Adam Experimental
                        if (m_updateEntitiesThread == null)
                        {
                            m_updateEntitiesThread = new Thread(m_sceneGraph.UpdateEntities);
                            
                            ThreadTracker.Add(m_updateEntitiesThread);
                        }

                        if (m_updateEntitiesThread.ThreadState == ThreadState.Stopped)
                            m_updateEntitiesThread.Start();
                        */
                        
                        m_sceneGraph.UpdateEntities();
                    }
                        

                    // run through entities that have scheduled themselves for
                    // updates looking for updates(faster)
                    if (m_frame % m_update_entitiesquick == 0)
                        m_sceneGraph.ProcessUpdates();

                    // Run through scenepresences looking for updates
                    if (m_frame % m_update_presences == 0)
                        m_sceneGraph.UpdatePresences();

                    // Delete temp-on-rez stuff
                    if (m_frame % m_update_backup == 0)
                        CleanTempObjects();

                    if (RegionStatus != RegionStatus.SlaveScene)
                    {
                        if (m_frame % m_update_events == 0)
                            UpdateEvents();

                        if (m_frame % m_update_backup == 0)
                            UpdateStorageBackup();

                        if (m_frame % m_update_terrain == 0)
                            UpdateTerrain();

                        if (m_frame % m_update_land == 0)
                            UpdateLand();
                        
                        otherMS = Environment.TickCount - otherMS;
                        // if (m_frame%m_update_avatars == 0)
                        //   UpdateInWorldTime();
                        StatsReporter.AddPhysicsFPS(physicsFPS);
                        StatsReporter.AddTimeDilation(m_timedilation);
                        StatsReporter.AddFPS(1);
                        StatsReporter.AddInPackets(0);
                        StatsReporter.SetRootAgents(m_sceneGraph.GetRootAgentCount());
                        StatsReporter.SetChildAgents(m_sceneGraph.GetChildAgentCount());
                        StatsReporter.SetObjects(m_sceneGraph.GetTotalObjectsCount());
                        StatsReporter.SetActiveObjects(m_sceneGraph.GetActiveObjectsCount());
                        frameMS = Environment.TickCount - frameMS;
                        StatsReporter.addFrameMS(frameMS);
                        StatsReporter.addPhysicsMS(physicsMS);
                        StatsReporter.addOtherMS(otherMS);
                        StatsReporter.SetActiveScripts(m_sceneGraph.GetActiveScriptsCount());
                        StatsReporter.addScriptLines(m_sceneGraph.GetScriptLPS());
                    }
                }
                catch (NotImplementedException)
                {
                    throw;
                }
                catch (AccessViolationException e)
                {
                    m_log.Error("[Scene]: Failed with exception " + e.ToString() + " On Region: " + RegionInfo.RegionName);
                }
                //catch (NullReferenceException e)
                //{
                //   m_log.Error("[Scene]: Failed with exception " + e.ToString() + " On Region: " + RegionInfo.RegionName);
                //}
                catch (InvalidOperationException e)
                {
                    m_log.Error("[Scene]: Failed with exception " + e.ToString() + " On Region: " + RegionInfo.RegionName);
                }
                catch (Exception e)
                {
                    m_log.Error("[Scene]: Failed with exception " + e.ToString() + " On Region: " + RegionInfo.RegionName);
                }
                finally
                {
                    //updateLock.ReleaseMutex();
                    // Get actual time dilation
                    float tmpval = (m_timespan / (float)SinceLastFrame.TotalSeconds);

                    // If actual time dilation is greater then one, we're catching up, so subtract
                    // the amount that's greater then 1 from the time dilation
                    if (tmpval > 1.0)
                    {
                        tmpval = tmpval - (tmpval - 1.0f);
                    }
                    m_timedilation = tmpval;

                    m_lastupdate = DateTime.Now;
                }
                maintc = Environment.TickCount - maintc;
                maintc = (int)(m_timespan * 1000) - maintc;

                if ((maintc < (m_timespan * 1000)) && maintc > 0)
                    Thread.Sleep(maintc);
            }
        }

        private void SendSimStatsPackets(SimStats stats)
        {
            List<ScenePresence> StatSendAgents = GetScenePresences();
            foreach (ScenePresence agent in StatSendAgents)
            {
                if (!agent.IsChildAgent)
                {
                    agent.ControllingClient.SendSimStats(stats);
                }
            }
        }

        private void UpdateLand()
        {
            if (LandChannel != null)
            {
                if (LandChannel.IsLandPrimCountTainted())
                {
                    EventManager.TriggerParcelPrimCountUpdate();
                }
            }
        }

        private void UpdateTerrain()
        {
            EventManager.TriggerTerrainTick();
        }

        private void UpdateStorageBackup()
        {
            if (!m_backingup)
            {
                m_backingup = true;
                Thread backupthread = new Thread(Backup);
                backupthread.Name = "BackupWriter";
                backupthread.IsBackground = true;
                backupthread.Start();
            }
        }

        private void UpdateEvents()
        {
            m_eventManager.TriggerOnFrame();
        }

        /// <summary>
        /// Perform delegate action on all clients subscribing to updates from this region.
        /// </summary>
        /// <returns></returns>
        public void Broadcast(Action<IClientAPI> whatToDo)
        {
            ForEachScenePresence(delegate(ScenePresence presence) { whatToDo(presence.ControllingClient); });
        }

        /// <summary>
        /// Backup the scene.  This acts as the main method of the backup thread.
        /// </summary>
        /// <returns></returns>
        public void Backup()
        {
            lock (m_returns)
            {
                EventManager.TriggerOnBackup(m_storageManager.DataStore);
                m_backingup = false;

                foreach (KeyValuePair<UUID, ReturnInfo> ret in m_returns)
                {
                    UUID transaction = UUID.Random();

                    GridInstantMessage msg = new GridInstantMessage();
                    msg.fromAgentID = new Guid(UUID.Zero.ToString()); // From server
                    msg.toAgentID = new Guid(ret.Key.ToString());
                    msg.imSessionID = new Guid(transaction.ToString());
                    msg.timestamp = (uint)Util.UnixTimeSinceEpoch();
                    msg.fromAgentName = "Server";
                    msg.dialog = (byte)19; // Object msg
                    msg.fromGroup = false;
                    msg.offline = (byte)1;
                    msg.ParentEstateID = RegionInfo.EstateSettings.ParentEstateID;
                    msg.Position = Vector3.Zero;
                    msg.RegionID = RegionInfo.RegionID.Guid;
                    msg.binaryBucket = new byte[0];
                    if (ret.Value.count > 1)
                        msg.message = string.Format("Your {0} objects were returned from {1} in region {2} due to {3}", ret.Value.count, ret.Value.location.ToString(), RegionInfo.RegionName, ret.Value.reason);
                    else
                        msg.message = string.Format("Your object {0} was returned from {1} in region {2} due to {3}", ret.Value.objectName, ret.Value.location.ToString(), RegionInfo.RegionName, ret.Value.reason);

                    IMessageTransferModule tr = RequestModuleInterface<IMessageTransferModule>();
                    if (tr != null)
                        tr.SendInstantMessage(msg, delegate(bool success) {} );
                }
                m_returns.Clear();
            }
        }

        public void ForceSceneObjectBackup(SceneObjectGroup group)
        {
            if (group != null)
            {
                group.ProcessBackup(m_storageManager.DataStore, true);
            }
        }

        public void AddReturn(UUID agentID, string objectName, Vector3 location, string reason)
        {
            lock (m_returns)
            {
                if (m_returns.ContainsKey(agentID))
                {
                    ReturnInfo info = m_returns[agentID];
                    info.count++;
                    m_returns[agentID] = info;
                }
                else
                {
                    ReturnInfo info = new ReturnInfo();
                    info.count = 1;
                    info.objectName = objectName;
                    info.location = location;
                    info.reason = reason;
                    m_returns[agentID] = info;
                }
            }
        }

        #endregion

        #region Load Terrain

        public void SaveTerrain()
        {
            m_storageManager.DataStore.StoreTerrain(Heightmap.GetDoubles(), RegionInfo.RegionID);
        }

        /// <summary>
        /// Loads the World heightmap
        /// </summary>
        public override void LoadWorldMap()
        {
            try
            {
                double[,] map = m_storageManager.DataStore.LoadTerrain(RegionInfo.RegionID);
                if (map == null)
                {
                    m_log.Info("[TERRAIN]: No default terrain. Generating a new terrain.");
                    Heightmap = new TerrainChannel();

                    m_storageManager.DataStore.StoreTerrain(Heightmap.GetDoubles(), RegionInfo.RegionID);
                }
                else
                {
                    Heightmap = new TerrainChannel(map);
                }
            }
            catch (Exception e)
            {
                m_log.Warn("[TERRAIN]: Scene.cs: LoadWorldMap() - Failed with exception " + e.ToString());
            }
        }

        /// <summary>
        /// Register this region with a grid service
        /// </summary>
        /// <exception cref="System.Exception">Thrown if registration of the region itself fails.</exception>
        public void RegisterRegionWithGrid()
        {
            RegisterCommsEvents();

            // These two 'commands' *must be* next to each other or sim rebooting fails.
            m_sceneGridService.RegisterRegion(m_interregionCommsOut, RegionInfo);
            m_sceneGridService.InformNeighborsThatRegionisUp(RegionInfo);

            Dictionary<string, string> dGridSettings = m_sceneGridService.GetGridSettings();

            if (dGridSettings.ContainsKey("allow_forceful_banlines"))
            {
                if (dGridSettings["allow_forceful_banlines"] != "TRUE")
                {
                    m_log.Info("[GRID]: Grid is disabling forceful parcel banlists");
                    EventManager.TriggerSetAllowForcefulBan(false);
                }
                else
                {
                    m_log.Info("[GRID]: Grid is allowing forceful parcel banlists");
                    EventManager.TriggerSetAllowForcefulBan(true);
                }
            }
        }

        /// <summary>
        /// Create a terrain texture for this scene
        /// </summary>
        public void CreateTerrainTexture(bool temporary)
        {
            //create a texture asset of the terrain
            IMapImageGenerator terrain = RequestModuleInterface<IMapImageGenerator>();

            // Cannot create a map for a nonexistant heightmap yet.
            if (Heightmap == null)
                return;

            if (terrain == null)
                return;

            byte[] data = terrain.WriteJpeg2000Image("defaultstripe.png");
            if (data != null)
            {
                IWorldMapModule mapModule = RequestModuleInterface<IWorldMapModule>();
                                
                if (mapModule != null)    
                    mapModule.LazySaveGeneratedMaptile(data, temporary);
            }
        }

        #endregion

        #region Load Land

        public void loadAllLandObjectsFromStorage(UUID regionID)
        {
            m_log.Info("[SCENE]: Loading land objects from storage");
            List<LandData> landData = m_storageManager.DataStore.LoadLandObjects(regionID);

            if (LandChannel != null)
            {
                if (landData.Count == 0)
                {
                    EventManager.TriggerNoticeNoLandDataFromStorage();
                }
                else
                {
                    EventManager.TriggerIncomingLandDataFromStorage(landData);
                }
            }
            else
            {
                m_log.Error("[SCENE]: Land Channel is not defined. Cannot load from storage!");
            }
        }

        #endregion

        #region Primitives Methods

        /// <summary>
        /// Loads the World's objects
        /// </summary>
        public virtual void LoadPrimsFromStorage(UUID regionID)
        {
            m_log.Info("[SCENE]: Loading objects from datastore");

            List<SceneObjectGroup> PrimsFromDB = m_storageManager.DataStore.LoadObjects(regionID);
            foreach (SceneObjectGroup group in PrimsFromDB)
            {
                if (group.RootPart == null)
                {
                    m_log.ErrorFormat("[SCENE] Found a SceneObjectGroup with m_rootPart == null and {0} children",
                                      group.Children == null ? 0 : group.Children.Count);
                }

                AddRestoredSceneObject(group, true, true);
                SceneObjectPart rootPart = group.GetChildPart(group.UUID);
                rootPart.ObjectFlags &= ~(uint)PrimFlags.Scripted;
                rootPart.TrimPermissions();
                group.CheckSculptAndLoad();
                //rootPart.DoPhysicsPropertyUpdate(UsePhysics, true);
            }

            m_log.Info("[SCENE]: Loaded " + PrimsFromDB.Count.ToString() + " SceneObject(s)");
        }

        public Vector3 GetNewRezLocation(Vector3 RayStart, Vector3 RayEnd, UUID RayTargetID, Quaternion rot, byte bypassRayCast, byte RayEndIsIntersection, bool frontFacesOnly, Vector3 scale, bool FaceCenter)
        {
            Vector3 pos = Vector3.Zero;
            if (RayEndIsIntersection == (byte)1)
            {
                pos = RayEnd;
                return pos;
            }

            if (RayTargetID != UUID.Zero)
            {
                SceneObjectPart target = GetSceneObjectPart(RayTargetID);

                Vector3 direction = Vector3.Normalize(RayEnd - RayStart);
                Vector3 AXOrigin = new Vector3(RayStart.X, RayStart.Y, RayStart.Z);
                Vector3 AXdirection = new Vector3(direction.X, direction.Y, direction.Z);

                if (target != null)
                {
                    pos = target.AbsolutePosition;
                    //m_log.Info("[OBJECT_REZ]: TargetPos: " + pos.ToString() + ", RayStart: " + RayStart.ToString() + ", RayEnd: " + RayEnd.ToString() + ", Volume: " + Util.GetDistanceTo(RayStart,RayEnd).ToString() + ", mag1: " + Util.GetMagnitude(RayStart).ToString() + ", mag2: " + Util.GetMagnitude(RayEnd).ToString());

                    // TODO: Raytrace better here

                    //EntityIntersection ei = m_sceneGraph.GetClosestIntersectingPrim(new Ray(AXOrigin, AXdirection));
                    Ray NewRay = new Ray(AXOrigin, AXdirection);

                    // Ray Trace against target here
                    EntityIntersection ei = target.TestIntersectionOBB(NewRay, Quaternion.Identity, frontFacesOnly, FaceCenter);

                    // Un-comment out the following line to Get Raytrace results printed to the console.
                   // m_log.Info("[RAYTRACERESULTS]: Hit:" + ei.HitTF.ToString() + " Point: " + ei.ipoint.ToString() + " Normal: " + ei.normal.ToString());
                    float ScaleOffset = 0.5f;

                    // If we hit something
                    if (ei.HitTF)
                    {
                        Vector3 scaleComponent = new Vector3(ei.AAfaceNormal.X, ei.AAfaceNormal.Y, ei.AAfaceNormal.Z);
                        if (scaleComponent.X != 0) ScaleOffset = scale.X;
                        if (scaleComponent.Y != 0) ScaleOffset = scale.Y;
                        if (scaleComponent.Z != 0) ScaleOffset = scale.Z;
                        ScaleOffset = Math.Abs(ScaleOffset);
                        Vector3 intersectionpoint = new Vector3(ei.ipoint.X, ei.ipoint.Y, ei.ipoint.Z);
                        Vector3 normal = new Vector3(ei.normal.X, ei.normal.Y, ei.normal.Z);
                        // Set the position to the intersection point
                        Vector3 offset = (normal * (ScaleOffset / 2f));
                        pos = (intersectionpoint + offset);

                        // Un-offset the prim (it gets offset later by the consumer method)
                        pos.Z -= 0.25F;
                    }

                    return pos;
                }
                else
                {
                    // We don't have a target here, so we're going to raytrace all the objects in the scene.

                    EntityIntersection ei = m_sceneGraph.GetClosestIntersectingPrim(new Ray(AXOrigin, AXdirection), true, false);

                    // Un-comment the following line to print the raytrace results to the console.
                    //m_log.Info("[RAYTRACERESULTS]: Hit:" + ei.HitTF.ToString() + " Point: " + ei.ipoint.ToString() + " Normal: " + ei.normal.ToString());

                    if (ei.HitTF)
                    {
                        pos = new Vector3(ei.ipoint.X, ei.ipoint.Y, ei.ipoint.Z);
                    } else
                    {
                        // fall back to our stupid functionality
                        pos = RayEnd;
                    }

                    return pos;
                }
            }
            else
            {
                // fall back to our stupid functionality
                pos = RayEnd;
                return pos;
            }
        }

        public virtual void AddNewPrim(UUID ownerID, UUID groupID, Vector3 RayEnd, Quaternion rot, PrimitiveBaseShape shape,
                                       byte bypassRaycast, Vector3 RayStart, UUID RayTargetID,
                                       byte RayEndIsIntersection)
        {
            Vector3 pos = GetNewRezLocation(RayStart, RayEnd, RayTargetID, rot, bypassRaycast, RayEndIsIntersection, true, new Vector3(0.5f, 0.5f, 0.5f), false);

            if (Permissions.CanRezObject(1, ownerID, pos))
            {
                // rez ON the ground, not IN the ground
                pos.Z += 0.25F;

                AddNewPrim(ownerID, groupID, pos, rot, shape);
            }
        }

        public virtual SceneObjectGroup AddNewPrim(
            UUID ownerID, UUID groupID, Vector3 pos, Quaternion rot, PrimitiveBaseShape shape)
        {
            //m_log.DebugFormat(
            //    "[SCENE]: Scene.AddNewPrim() pcode {0} called for {1} in {2}", shape.PCode, ownerID, RegionInfo.RegionName);

            // If an entity creator has been registered for this prim type then use that
            if (m_entityCreators.ContainsKey((PCode)shape.PCode))
                return m_entityCreators[(PCode)shape.PCode].CreateEntity(ownerID, groupID, pos, rot, shape);

            // Otherwise, use this default creation code;
            SceneObjectGroup sceneObject = new SceneObjectGroup(ownerID, pos, rot, shape);
            AddNewSceneObject(sceneObject, true);
            sceneObject.SetGroup(groupID, null);

            return sceneObject;
        }

        /// <summary>
        /// Add an object into the scene that has come from storage
        /// </summary>
        /// 
        /// <param name="sceneObject"></param>
        /// <param name="attachToBackup">
        /// If true, changes to the object will be reflected in its persisted data
        /// If false, the persisted data will not be changed even if the object in the scene is changed
        /// </param>
        /// <param name="alreadyPersisted">
        /// If true, we won't persist this object until it changes
        /// If false, we'll persist this object immediately
        /// </param>
        /// <returns>
        /// true if the object was added, false if an object with the same uuid was already in the scene
        /// </returns>
        public bool AddRestoredSceneObject(
            SceneObjectGroup sceneObject, bool attachToBackup, bool alreadyPersisted)
        {
            return m_sceneGraph.AddRestoredSceneObject(sceneObject, attachToBackup, alreadyPersisted);
        }

        /// <summary>
        /// Add a newly created object to the scene
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <param name="attachToBackup">
        /// If true, the object is made persistent into the scene.
        /// If false, the object will not persist over server restarts
        /// </param>
        public bool AddNewSceneObject(SceneObjectGroup sceneObject, bool attachToBackup)
        {
            return m_sceneGraph.AddNewSceneObject(sceneObject, attachToBackup);
        }

        /// <summary>
        /// Delete every object from the scene
        /// </summary>
        public void DeleteAllSceneObjects()
        {
            lock (Entities)
            {
                ICollection<EntityBase> entities = new List<EntityBase>(Entities);

                foreach (EntityBase e in entities)
                {
                    if (e is SceneObjectGroup)
                        DeleteSceneObject((SceneObjectGroup)e, false);
                }
            }
        }

        /// <summary>
        /// Synchronously delete the given object from the scene.
        /// </summary>
        /// <param name="group">Object Id</param>
        /// <param name="silent">Suppress broadcasting changes to other clients.</param>
        public void DeleteSceneObject(SceneObjectGroup group, bool silent)
        {
            //SceneObjectPart rootPart = group.GetChildPart(group.UUID);

            // Serialize calls to RemoveScriptInstances to avoid
            // deadlocking on m_parts inside SceneObjectGroup
            lock (m_deleting_scene_object)
            {
                group.RemoveScriptInstances();
            }

            foreach (SceneObjectPart part in group.Children.Values)
            {
                if (part.IsJoint() && ((part.ObjectFlags&(uint)PrimFlags.Physics) != 0) )
                {
                    PhysicsScene.RequestJointDeletion(part.Name); // FIXME: what if the name changed?
                }
                else if (part.PhysActor != null)
                {
                    PhysicsScene.RemovePrim(part.PhysActor);
                    part.PhysActor = null;
                }
            }
//            if (rootPart.PhysActor != null)
//            {
//                PhysicsScene.RemovePrim(rootPart.PhysActor);
//                rootPart.PhysActor = null;
//            }

            if (UnlinkSceneObject(group.UUID, false))
            {
                EventManager.TriggerObjectBeingRemovedFromScene(group);
                EventManager.TriggerParcelPrimCountTainted();
            }

            group.DeleteGroup(silent);
        }

        /// <summary>
        /// Unlink the given object from the scene.  Unlike delete, this just removes the record of the object - the
        /// object itself is not destroyed.
        /// </summary>
        /// <param name="uuid">Id of object.</param>
        /// <returns>true if the object was in the scene, false if it was not</returns>
        /// <param name="softDelete">If true, only deletes from scene, but keeps object in database.</param>
        public bool UnlinkSceneObject(UUID uuid, bool softDelete)
        {
            if (m_sceneGraph.DeleteSceneObject(uuid, softDelete))
            {
                if (!softDelete)
                {
                    m_storageManager.DataStore.RemoveObject(uuid,
                                                            m_regInfo.RegionID);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Move the given scene object into a new region depending on which region its absolute position has moved
        /// into.
        ///
        /// This method locates the new region handle and offsets the prim position for the new region
        /// </summary>
        /// <param name="attemptedPosition">the attempted out of region position of the scene object</param>
        /// <param name="grp">the scene object that we're crossing</param>
        public void CrossPrimGroupIntoNewRegion(Vector3 attemptedPosition, SceneObjectGroup grp, bool silent)
        {
            if (grp == null)
                return;
            if (grp.IsDeleted)
                return;

            if (grp.RootPart.DIE_AT_EDGE)
            {
                // We remove the object here
                try
                {
                    DeleteSceneObject(grp, false);
                }
                catch (Exception)
                {
                    m_log.Warn("[DATABASE]: exception when trying to remove the prim that crossed the border.");
                }
                return;
            }

            int thisx = (int)RegionInfo.RegionLocX;
            int thisy = (int)RegionInfo.RegionLocY;

            ulong newRegionHandle = 0;
            Vector3 pos = attemptedPosition;

            if (attemptedPosition.X > Constants.RegionSize + 0.1f)
            {
                pos.X = ((pos.X - Constants.RegionSize));
                newRegionHandle
                    = Util.UIntsToLong((uint)((thisx + 1) * Constants.RegionSize), (uint)(thisy * Constants.RegionSize));
                // x + 1
            }
            else if (attemptedPosition.X < -0.1f)
            {
                pos.X = ((pos.X + Constants.RegionSize));
                newRegionHandle
                    = Util.UIntsToLong((uint)((thisx - 1) * Constants.RegionSize), (uint)(thisy * Constants.RegionSize));
                // x - 1
            }

            if (attemptedPosition.Y > Constants.RegionSize + 0.1f)
            {
                pos.Y = ((pos.Y - Constants.RegionSize));
                newRegionHandle
                    = Util.UIntsToLong((uint)(thisx * Constants.RegionSize), (uint)((thisy + 1) * Constants.RegionSize));
                // y + 1
            }
            else if (attemptedPosition.Y < -0.1f)
            {
                pos.Y = ((pos.Y + Constants.RegionSize));
                newRegionHandle
                    = Util.UIntsToLong((uint)(thisx * Constants.RegionSize), (uint)((thisy - 1) * Constants.RegionSize));
                // y - 1
            }

            // Offset the positions for the new region across the border
            Vector3 oldGroupPosition = grp.RootPart.GroupPosition;
            grp.OffsetForNewRegion(pos);

            // If we fail to cross the border, then reset the position of the scene object on that border.
            if (!CrossPrimGroupIntoNewRegion(newRegionHandle, grp, silent))
            {
                grp.OffsetForNewRegion(oldGroupPosition);
                grp.ScheduleGroupForFullUpdate();
            }
        }

        /// <summary>
        /// Move the given scene object into a new region
        /// </summary>
        /// <param name="newRegionHandle"></param>
        /// <param name="grp">Scene Object Group that we're crossing</param>
        /// <returns>
        /// true if the crossing itself was successful, false on failure
        /// FIMXE: we still return true if the crossing object was not successfully deleted from the originating region
        /// </returns>
        public bool CrossPrimGroupIntoNewRegion(ulong newRegionHandle, SceneObjectGroup grp, bool silent)
        {
            //m_log.Debug("  >>> CrossPrimGroupIntoNewRegion <<<");

            bool successYN = false;
            grp.RootPart.UpdateFlag = 0;
            //int primcrossingXMLmethod = 0;

            if (newRegionHandle != 0)
            {
                //string objectState = grp.GetStateSnapshot();

                //successYN
                //    = m_sceneGridService.PrimCrossToNeighboringRegion(
                //        newRegionHandle, grp.UUID, m_serializer.SaveGroupToXml2(grp), primcrossingXMLmethod);
                //if (successYN && (objectState != "") && m_allowScriptCrossings)
                //{
                //    successYN = m_sceneGridService.PrimCrossToNeighboringRegion(
                //            newRegionHandle, grp.UUID, objectState, 100);
                //}

                // And the new channel...
                if (m_interregionCommsOut != null)
                    successYN = m_interregionCommsOut.SendCreateObject(newRegionHandle, grp, true);

                if (successYN)
                {
                    // We remove the object here
                    try
                    {
                        DeleteSceneObject(grp, silent);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[INTERREGION]: Exception deleting the old object left behind on a border crossing for {0}, {1}",
                            grp, e);
                    }
                }
                else
                {
                    if (!grp.IsDeleted)
                    {
                        if (grp.RootPart.PhysActor != null)
                        {
                            grp.RootPart.PhysActor.CrossingFailure();
                        }
                    }

                    m_log.ErrorFormat("[INTERREGION]: Prim crossing failed for {0}", grp);
                }
            }
            else
            {
                m_log.Error("[INTERREGION]: region handle was unexpectedly 0 in Scene.CrossPrimGroupIntoNewRegion()");
            }

            return successYN;
        }

        /// <summary>
        /// Handle a scene object that is crossing into this region from another.
        /// NOTE: Unused as of 2009-02-09. Soon to be deleted.
        /// </summary>
        /// <param name="regionHandle"></param>
        /// <param name="primID"></param>
        /// <param name="objXMLData"></param>
        /// <param name="XMLMethod"></param>
        /// <returns></returns>
        public bool IncomingInterRegionPrimGroup(UUID primID, string objXMLData, int XMLMethod)
        {

            if (XMLMethod == 0)
            {
                m_log.DebugFormat("[INTERREGION]: A new prim {0} arrived from a neighbor", primID);
                SceneObjectGroup sceneObject = m_serializer.DeserializeGroupFromXml2(objXMLData);

                return AddSceneObject(primID, sceneObject);

            }
            else if ((XMLMethod == 100) && m_allowScriptCrossings)
            {
                m_log.Warn("[INTERREGION]: Prim state data arrived from a neighbor");

                XmlDocument doc = new XmlDocument();
                doc.LoadXml(objXMLData);

                XmlNodeList rootL = doc.GetElementsByTagName("ScriptData");
                if (rootL.Count == 1)
                {
                    XmlNode rootNode = rootL[0];
                    if (rootNode != null)
                    {
                        XmlNodeList partL = rootNode.ChildNodes;

                        foreach (XmlNode part in partL)
                        {
                            XmlNodeList nodeL = part.ChildNodes;

                            switch (part.Name)
                            {
                            case "Assemblies":
                                foreach (XmlNode asm in nodeL)
                                {
                                    string fn = asm.Attributes.GetNamedItem("Filename").Value;

                                    Byte[] filedata = Convert.FromBase64String(asm.InnerText);
                                    string path = Path.Combine("ScriptEngines", RegionInfo.RegionID.ToString());
                                    path = Path.Combine(path, fn);

                                    if (!File.Exists(path))
                                    {
                                        FileStream fs = File.Create(path);
                                        fs.Write(filedata, 0, filedata.Length);
                                        fs.Close();
                                    }
                                }
                                break;
                            case "ScriptStates":
                                foreach (XmlNode st in nodeL)
                                {
                                    string id = st.Attributes.GetNamedItem("UUID").Value;
                                    UUID uuid = new UUID(id);
                                    XmlNode state = st.ChildNodes[0];

                                    XmlDocument sdoc = new XmlDocument();
                                    XmlNode sxmlnode = sdoc.CreateNode(
                                            XmlNodeType.XmlDeclaration,
                                            "", "");
                                    sdoc.AppendChild(sxmlnode);

                                    XmlNode newnode = sdoc.ImportNode(state, true);
                                    sdoc.AppendChild(newnode);

                                    string spath = Path.Combine("ScriptEngines", RegionInfo.RegionID.ToString());
                                    spath = Path.Combine(spath, uuid.ToString());
                                    FileStream sfs = File.Create(spath + ".state");
                                    ASCIIEncoding enc = new ASCIIEncoding();
                                    Byte[] buf = enc.GetBytes(sdoc.InnerXml);
                                    sfs.Write(buf, 0, buf.Length);
                                    sfs.Close();
                                }
                                break;
                            }
                        }
                    }
                }

                SceneObjectPart RootPrim = GetSceneObjectPart(primID);
                RootPrim.ParentGroup.CreateScriptInstances(0, false, DefaultScriptEngine, 1);

                return true;
            }

            return true;
        }

        public bool IncomingCreateObject(ISceneObject sog)
        {
            //m_log.Debug(" >>> IncomingCreateObject <<< " + ((SceneObjectGroup)sog).AbsolutePosition + " deleted? " + ((SceneObjectGroup)sog).IsDeleted);
            SceneObjectGroup newObject;
            try
            {
                newObject = (SceneObjectGroup)sog;
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[SCENE]: Problem casting object: {0}", e.Message);
                return false;
            }

            if (!AddSceneObject(newObject.UUID, newObject))
            {
                m_log.DebugFormat("[SCENE]: Problem adding scene object {0} in {1} ", sog.UUID, RegionInfo.RegionName);
                return false;
            }
            newObject.RootPart.ParentGroup.CreateScriptInstances(0, false, DefaultScriptEngine, 1);
            return true;
        }

        public virtual bool IncomingCreateObject(UUID userID, UUID itemID)
        {
            ScenePresence sp = GetScenePresence(userID);
            if (sp != null)
            {
                uint attPt = (uint)sp.Appearance.GetAttachpoint(itemID);
                m_sceneGraph.RezSingleAttachment(sp.ControllingClient, itemID, attPt);
            }
            
            return false;
        }

        public bool AddSceneObject(UUID primID, SceneObjectGroup sceneObject)
        {
            // If the user is banned, we won't let any of their objects
            // enter. Period.
            //
            if (m_regInfo.EstateSettings.IsBanned(sceneObject.OwnerID))
            {
                m_log.Info("[INTERREGION]: Denied prim crossing for " +
                        "banned avatar");

                return false;
            }
            // Force allocation of new LocalId
            //
            foreach (SceneObjectPart p in sceneObject.Children.Values)
                p.LocalId = 0;

            if (sceneObject.RootPart.Shape.PCode == (byte)PCode.Prim)
            {
                if (sceneObject.RootPart.Shape.State != 0) // Attchment
                {

                    sceneObject.RootPart.AddFlag(PrimFlags.TemporaryOnRez);

                    AddRestoredSceneObject(sceneObject, false, false);

                    // Handle attachment special case
                    //
                    //SceneObjectPart RootPrim = GetSceneObjectPart(primID);
                    SceneObjectPart RootPrim = sceneObject.RootPart;

                    // Fix up attachment Parent Local ID
                    //
                    ScenePresence sp = GetScenePresence(sceneObject.OwnerID);

                    //uint parentLocalID = 0;
                    if (sp != null)
                    {
                        //parentLocalID = sp.LocalId;

                        //sceneObject.RootPart.IsAttachment = true;
                        //sceneObject.RootPart.SetParentLocalId(parentLocalID);

                        SceneObjectGroup grp = sceneObject;

                        //RootPrim.SetParentLocalId(parentLocalID);

                        m_log.DebugFormat("[ATTACHMENT]: Received " +
                                    "attachment {0}, inworld asset id {1}",
                                    //grp.RootPart.LastOwnerID.ToString(),
                                    grp.GetFromAssetID(),
                                    grp.UUID.ToString());

                        //grp.SetFromAssetID(grp.RootPart.LastOwnerID);
                        m_log.DebugFormat("[ATTACHMENT]: Attach " +
                                "to avatar {0} at position {1}",
                                sp.UUID.ToString(), grp.AbsolutePosition);
                        AttachObject(sp.ControllingClient,
                                grp.LocalId, (uint)0,
                                grp.GroupRotation,
                                grp.AbsolutePosition, false);
                        RootPrim.RemFlag(PrimFlags.TemporaryOnRez);
                        grp.SendGroupFullUpdate();
                    }
                    else
                    {
                        RootPrim.RemFlag(PrimFlags.TemporaryOnRez);
                        RootPrim.AddFlag(PrimFlags.TemporaryOnRez);
                    }
                    
                }
                else
                {
                    AddRestoredSceneObject(sceneObject, true, false);

                    if (!Permissions.CanObjectEntry(sceneObject.UUID,
                            true, sceneObject.AbsolutePosition))
                    {
                        // Deny non attachments based on parcel settings
                        //
                        m_log.Info("[INTERREGION]: Denied prim crossing " +
                                "because of parcel settings");

                        DeleteSceneObject(sceneObject, false);

                        return false;
                    }
                }
            }
            return true;
        }
        #endregion

        #region Add/Remove Avatar Methods

        public override void AddNewClient(IClientAPI client)
        {
            SubscribeToClientEvents(client);
            ScenePresence presence;

            if (m_restorePresences.ContainsKey(client.AgentId))
            {
                m_log.DebugFormat("[SCENE]: Restoring agent {0} {1} in {2}", client.Name, client.AgentId, RegionInfo.RegionName);
                
                presence = m_restorePresences[client.AgentId];
                m_restorePresences.Remove(client.AgentId);
                
                // This is one of two paths to create avatars that are
                // used.  This tends to get called more in standalone
                // than grid, not really sure why, but as such needs
                // an explicity appearance lookup here.
                AvatarAppearance appearance = null;
                GetAvatarAppearance(client, out appearance);
                presence.Appearance = appearance;
                
                presence.initializeScenePresence(client, RegionInfo, this);
                
                m_sceneGraph.AddScenePresence(presence);
                
                lock (m_restorePresences)
                {
                    Monitor.PulseAll(m_restorePresences);
                }
            }
            else
            {
                m_log.DebugFormat(
                    "[SCENE]: Adding new child agent for {0} in {1}",
                    client.Name, RegionInfo.RegionName);
                
                CommsManager.UserProfileCacheService.AddNewUser(client.AgentId);
                
                CreateAndAddScenePresence(client);
            }

            m_LastLogin = Environment.TickCount;
            EventManager.TriggerOnNewClient(client);
        }

        protected virtual void SubscribeToClientEvents(IClientAPI client)
        {
            client.OnRegionHandShakeReply += SendLayerData;
            client.OnAddPrim += AddNewPrim;
            client.OnUpdatePrimGroupPosition += m_sceneGraph.UpdatePrimPosition;
            client.OnUpdatePrimSinglePosition += m_sceneGraph.UpdatePrimSinglePosition;
            client.OnUpdatePrimGroupRotation += m_sceneGraph.UpdatePrimRotation;
            client.OnUpdatePrimGroupMouseRotation += m_sceneGraph.UpdatePrimRotation;
            client.OnUpdatePrimSingleRotation += m_sceneGraph.UpdatePrimSingleRotation;
            client.OnUpdatePrimScale += m_sceneGraph.UpdatePrimScale;
            client.OnUpdatePrimGroupScale += m_sceneGraph.UpdatePrimGroupScale;
            client.OnUpdateExtraParams += m_sceneGraph.UpdateExtraParam;
            client.OnUpdatePrimShape += m_sceneGraph.UpdatePrimShape;
            client.OnUpdatePrimTexture += m_sceneGraph.UpdatePrimTexture;
            client.OnTeleportLocationRequest += RequestTeleportLocation;
            client.OnTeleportLandmarkRequest += RequestTeleportLandmark;
            client.OnObjectSelect += SelectPrim;
            client.OnObjectDeselect += DeselectPrim;
            client.OnGrabUpdate += m_sceneGraph.MoveObject;
            client.OnSpinStart += m_sceneGraph.SpinStart;
            client.OnSpinUpdate += m_sceneGraph.SpinObject;
            client.OnDeRezObject += DeRezObject;
            client.OnRezObject += RezObject;
            client.OnRezSingleAttachmentFromInv += RezSingleAttachment;
            client.OnRezMultipleAttachmentsFromInv += RezMultipleAttachments;
            client.OnDetachAttachmentIntoInv += DetachSingleAttachmentToInv;
            client.OnObjectAttach += m_sceneGraph.AttachObject;
            client.OnObjectDetach += m_sceneGraph.DetachObject;
            client.OnObjectDrop += m_sceneGraph.DropObject;
            client.OnNameFromUUIDRequest += CommsManager.HandleUUIDNameRequest;
            client.OnObjectDescription += m_sceneGraph.PrimDescription;
            client.OnObjectName += m_sceneGraph.PrimName;
            client.OnObjectClickAction += m_sceneGraph.PrimClickAction;
            client.OnObjectMaterial += m_sceneGraph.PrimMaterial;
            client.OnLinkObjects += m_sceneGraph.LinkObjects;
            client.OnDelinkObjects += m_sceneGraph.DelinkObjects;
            client.OnObjectDuplicate += m_sceneGraph.DuplicateObject;
            client.OnObjectDuplicateOnRay += doObjectDuplicateOnRay;
            client.OnUpdatePrimFlags += m_sceneGraph.UpdatePrimFlags;
            client.OnRequestObjectPropertiesFamily += m_sceneGraph.RequestObjectPropertiesFamily;           
            client.OnObjectPermissions += HandleObjectPermissionsUpdate;
            client.OnCreateNewInventoryItem += CreateNewInventoryItem;
            client.OnCreateNewInventoryFolder += HandleCreateInventoryFolder;
            client.OnUpdateInventoryFolder += HandleUpdateInventoryFolder;
            client.OnMoveInventoryFolder += HandleMoveInventoryFolder;
            client.OnFetchInventoryDescendents += HandleFetchInventoryDescendents;
            client.OnPurgeInventoryDescendents += HandlePurgeInventoryDescendents;
            client.OnFetchInventory += HandleFetchInventory;
            client.OnUpdateInventoryItem += UpdateInventoryItemAsset;
            client.OnCopyInventoryItem += CopyInventoryItem;
            client.OnMoveInventoryItem += MoveInventoryItem;
            client.OnRemoveInventoryItem += RemoveInventoryItem;
            client.OnRemoveInventoryFolder += RemoveInventoryFolder;
            client.OnRezScript += RezScript;
            client.OnRequestTaskInventory += RequestTaskInventory;
            client.OnRemoveTaskItem += RemoveTaskInventory;
            client.OnUpdateTaskInventory += UpdateTaskInventory;
            client.OnMoveTaskItem += ClientMoveTaskInventoryItem;
            client.OnGrabObject += ProcessObjectGrab;
            client.OnDeGrabObject += ProcessObjectDeGrab;
            client.OnMoneyTransferRequest += ProcessMoneyTransferRequest;
            client.OnParcelBuy += ProcessParcelBuy;
            client.OnAvatarPickerRequest += ProcessAvatarPickerRequest;
            client.OnObjectIncludeInSearch += m_sceneGraph.MakeObjectSearchable;
            client.OnTeleportHomeRequest += TeleportClientHome;
            client.OnSetStartLocationRequest += SetHomeRezPoint;
            client.OnUndo += m_sceneGraph.HandleUndo;
            client.OnObjectGroupRequest += m_sceneGraph.HandleObjectGroupUpdate;
            client.OnParcelReturnObjectsRequest += LandChannel.ReturnObjectsInParcel;
            client.OnParcelSetOtherCleanTime += LandChannel.SetParcelOtherCleanTime;
            client.OnObjectSaleInfo += ObjectSaleInfo;
            client.OnScriptReset += ProcessScriptReset;
            client.OnGetScriptRunning += GetScriptRunning;
            client.OnSetScriptRunning += SetScriptRunning;
            client.OnRegionHandleRequest += RegionHandleRequest;
            client.OnUnackedTerrain += TerrainUnAcked;
            client.OnObjectOwner += ObjectOwner;

            IGodsModule godsModule = RequestModuleInterface<IGodsModule>();            
            client.OnGodKickUser += godsModule.KickUser;
            client.OnRequestGodlikePowers += godsModule.RequestGodlikePowers; 

            client.OnNetworkStatsUpdate += StatsReporter.AddPacketsStats;

            // EventManager.TriggerOnNewClient(client);
        }

        /// <summary>
        /// Teleport an avatar to their home region
        /// </summary>
        /// <param name="agentId"></param>
        /// <param name="client"></param>
        public virtual void TeleportClientHome(UUID agentId, IClientAPI client)
        {
            UserProfileData UserProfile = CommsManager.UserService.GetUserProfile(agentId);
            if (UserProfile != null)
            {
                RegionInfo regionInfo = CommsManager.GridService.RequestNeighbourInfo(UserProfile.HomeRegionID);
                if (regionInfo == null)
                {
                    regionInfo = CommsManager.GridService.RequestNeighbourInfo(UserProfile.HomeRegion);
                    if (regionInfo != null) // home region can be away temporarily, too
                    {
                        UserProfile.HomeRegionID = regionInfo.RegionID;
                        CommsManager.UserService.UpdateUserProfile(UserProfile);
                    }
                }
                if (regionInfo == null)
                {
                    // can't find the Home region: Tell viewer and abort
                    client.SendTeleportFailed("Your home-region could not be found.");
                    return;
                }
                RequestTeleportLocation(
                    client, regionInfo.RegionHandle, UserProfile.HomeLocation, UserProfile.HomeLookAt,
                    (uint)(TPFlags.SetLastToTarget | TPFlags.ViaHome));
            }
        }

        public void doObjectDuplicateOnRay(uint localID, uint dupeFlags, UUID AgentID, UUID GroupID,
                                           UUID RayTargetObj, Vector3 RayEnd, Vector3 RayStart,
                                           bool BypassRaycast, bool RayEndIsIntersection, bool CopyCenters, bool CopyRotates)
        {
            Vector3 pos;
            const bool frontFacesOnly = true;
            //m_log.Info("HITTARGET: " + RayTargetObj.ToString() + ", COPYTARGET: " + localID.ToString());
            SceneObjectPart target = GetSceneObjectPart(localID);
            SceneObjectPart target2 = GetSceneObjectPart(RayTargetObj);

            if (target != null && target2 != null)
            {
                Vector3 direction = Vector3.Normalize(RayEnd - RayStart);
                Vector3 AXOrigin = new Vector3(RayStart.X, RayStart.Y, RayStart.Z);
                Vector3 AXdirection = new Vector3(direction.X, direction.Y, direction.Z);

                if (target2.ParentGroup != null)
                {
                    pos = target2.AbsolutePosition;
                    //m_log.Info("[OBJECT_REZ]: TargetPos: " + pos.ToString() + ", RayStart: " + RayStart.ToString() + ", RayEnd: " + RayEnd.ToString() + ", Volume: " + Util.GetDistanceTo(RayStart,RayEnd).ToString() + ", mag1: " + Util.GetMagnitude(RayStart).ToString() + ", mag2: " + Util.GetMagnitude(RayEnd).ToString());

                    // TODO: Raytrace better here

                    //EntityIntersection ei = m_sceneGraph.GetClosestIntersectingPrim(new Ray(AXOrigin, AXdirection));
                    Ray NewRay = new Ray(AXOrigin, AXdirection);

                    // Ray Trace against target here
                    EntityIntersection ei = target2.TestIntersectionOBB(NewRay, Quaternion.Identity, frontFacesOnly, CopyCenters);

                    // Un-comment out the following line to Get Raytrace results printed to the console.
                    //m_log.Info("[RAYTRACERESULTS]: Hit:" + ei.HitTF.ToString() + " Point: " + ei.ipoint.ToString() + " Normal: " + ei.normal.ToString());
                    float ScaleOffset = 0.5f;

                    // If we hit something
                    if (ei.HitTF)
                    {
                        Vector3 scale = target.Scale;
                        Vector3 scaleComponent = new Vector3(ei.AAfaceNormal.X, ei.AAfaceNormal.Y, ei.AAfaceNormal.Z);
                        if (scaleComponent.X != 0) ScaleOffset = scale.X;
                        if (scaleComponent.Y != 0) ScaleOffset = scale.Y;
                        if (scaleComponent.Z != 0) ScaleOffset = scale.Z;
                        ScaleOffset = Math.Abs(ScaleOffset);
                        Vector3 intersectionpoint = new Vector3(ei.ipoint.X, ei.ipoint.Y, ei.ipoint.Z);
                        Vector3 normal = new Vector3(ei.normal.X, ei.normal.Y, ei.normal.Z);
                        Vector3 offset = normal * (ScaleOffset / 2f);
                        pos = intersectionpoint + offset;

                        // stick in offset format from the original prim
                        pos = pos - target.ParentGroup.AbsolutePosition;
                        if (CopyRotates)
                        {
                            Quaternion worldRot = target2.GetWorldRotation();

                            // SceneObjectGroup obj = m_sceneGraph.DuplicateObject(localID, pos, target.GetEffectiveObjectFlags(), AgentID, GroupID, worldRot);
                            m_sceneGraph.DuplicateObject(localID, pos, target.GetEffectiveObjectFlags(), AgentID, GroupID, worldRot);
                            //obj.Rotation = worldRot;
                            //obj.UpdateGroupRotation(worldRot);
                        }
                        else
                        {
                            m_sceneGraph.DuplicateObject(localID, pos, target.GetEffectiveObjectFlags(), AgentID, GroupID);
                        }
                    }

                    return;
                }

                return;
            }
        }

        public virtual void SetHomeRezPoint(IClientAPI remoteClient, ulong regionHandle, Vector3 position, Vector3 lookAt, uint flags)
        {
            UserProfileData UserProfile = CommsManager.UserService.GetUserProfile(remoteClient.AgentId);
            if (UserProfile != null)
            {
                // I know I'm ignoring the regionHandle provided by the teleport location request.
                // reusing the TeleportLocationRequest delegate, so regionHandle isn't valid
                UserProfile.HomeRegionID = RegionInfo.RegionID;
                // TODO: The next line can be removed, as soon as only homeRegionID based UserServers are around.
                // TODO: The HomeRegion property can be removed then, too
                UserProfile.HomeRegion = RegionInfo.RegionHandle;
                UserProfile.HomeLocation = position;
                UserProfile.HomeLookAt = lookAt;
                CommsManager.UserService.UpdateUserProfile(UserProfile);

                // FUBAR ALERT: this needs to be "Home position set." so the viewer saves a home-screenshot.
                m_dialogModule.SendAlertToUser(remoteClient, "Home position set.");
            }
            else
            {
                m_dialogModule.SendAlertToUser(remoteClient, "Set Home request Failed.");
            }
        }

        /// <summary>
        /// Create a child agent scene presence and add it to this scene.
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        protected virtual ScenePresence CreateAndAddScenePresence(IClientAPI client)
        {
            AvatarAppearance appearance = null;
            GetAvatarAppearance(client, out appearance);

            ScenePresence avatar = m_sceneGraph.CreateAndAddChildScenePresence(client, appearance);
            //avatar.KnownRegions = GetChildrenSeeds(avatar.UUID);

            m_eventManager.TriggerOnNewPresence(avatar);

            return avatar;
        }

        /// <summary>
        /// Get the avatar apperance for the given client.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="appearance"></param>
        public void GetAvatarAppearance(IClientAPI client, out AvatarAppearance appearance)
        {
            AgentCircuitData aCircuit = m_authenticateHandler.GetAgentCircuitData(client.CircuitCode);

            if (aCircuit == null)
            {
                m_log.DebugFormat("[APPEARANCE] Client did not supply a circuit. Non-Linden? Creating default appearance.");
                appearance = new AvatarAppearance(client.AgentId);
                return;
            }

            appearance = aCircuit.Appearance;
            if (appearance == null)
            {
                m_log.DebugFormat("[APPEARANCE]: Appearance not found in {0}, returning default", RegionInfo.RegionName);
                appearance = new AvatarAppearance(client.AgentId);
            }
        
        }

        /// <summary>
        /// Remove the given client from the scene.
        /// </summary>
        /// <param name="agentID"></param>
        public override void RemoveClient(UUID agentID)
        {
            bool childagentYN = false;
            ScenePresence avatar = GetScenePresence(agentID);
            if (avatar != null)
            {
                childagentYN = avatar.IsChildAgent;
            }

            if (avatar.ParentID != 0)
            {
                avatar.StandUp();
            }

            try
            {
                m_log.DebugFormat(
                    "[SCENE]: Removing {0} agent {1} from region {2}",
                    (childagentYN ? "child" : "root"), agentID, RegionInfo.RegionName);

                m_sceneGraph.removeUserCount(!childagentYN);
                CapsModule.RemoveCapsHandler(agentID);

                if (avatar.Scene.NeedSceneCacheClear(avatar.UUID))
                {
                    CommsManager.UserProfileCacheService.RemoveUser(agentID);
                }

                if (!avatar.IsChildAgent)
                {
                    m_sceneGridService.LogOffUser(agentID, RegionInfo.RegionID, RegionInfo.RegionHandle, avatar.AbsolutePosition, avatar.Lookat);
                    //List<ulong> childknownRegions = new List<ulong>();
                    //List<ulong> ckn = avatar.KnownChildRegionHandles;
                    //for (int i = 0; i < ckn.Count; i++)
                    //{
                    //    childknownRegions.Add(ckn[i]);
                    //}
                    List<ulong> regions = new List<ulong>(avatar.KnownChildRegionHandles);
                    regions.Remove(RegionInfo.RegionHandle);
                    m_sceneGridService.SendCloseChildAgentConnections(agentID, regions);

                }
                m_eventManager.TriggerClientClosed(agentID);
            }
            catch (NullReferenceException)
            {
                // We don't know which count to remove it from
                // Avatar is already disposed :/
            }

            m_eventManager.TriggerOnRemovePresence(agentID);
            Broadcast(delegate(IClientAPI client)
                      {
                          try
                          {
                              client.SendKillObject(avatar.RegionHandle, avatar.LocalId);
                          }
                          catch (NullReferenceException)
                          {
                              //We can safely ignore null reference exceptions.  It means the avatar are dead and cleaned up anyway.
                          }
                      });

            ForEachScenePresence(
                delegate(ScenePresence presence) { presence.CoarseLocationChange(); });

            IAgentAssetTransactions agentTransactions = this.RequestModuleInterface<IAgentAssetTransactions>();
            if (agentTransactions != null)
            {
                agentTransactions.RemoveAgentAssetTransactions(agentID);
            }

            m_sceneGraph.RemoveScenePresence(agentID);

            try
            {
                avatar.Close();
            }
            catch (NullReferenceException)
            {
                //We can safely ignore null reference exceptions.  It means the avatar are dead and cleaned up anyway.
            }
            catch (Exception e)
            {
                m_log.Error("[SCENE] Scene.cs:RemoveClient exception: " + e.ToString());
            }

            // Remove client agent from profile, so new logins will work
            if (!childagentYN)
            {
                m_sceneGridService.ClearUserAgent(agentID);
            }

            m_authenticateHandler.RemoveCircuit(avatar.ControllingClient.CircuitCode);

            //m_log.InfoFormat("[SCENE] Memory pre  GC {0}", System.GC.GetTotalMemory(false));
            //m_log.InfoFormat("[SCENE] Memory post GC {0}", System.GC.GetTotalMemory(true));
        }

        public void HandleRemoveKnownRegionsFromAvatar(UUID avatarID, List<ulong> regionslst)
        {
            ScenePresence av = GetScenePresence(avatarID);
            if (av != null)
            {
                lock (av)
                {
                    for (int i = 0; i < regionslst.Count; i++)
                    {
                        av.KnownChildRegionHandles.Remove(regionslst[i]);
                    }
                }
            }
        }

        public override void CloseAllAgents(uint circuitcode)
        {
            // Called by ClientView to kill all circuit codes
            ClientManager.CloseAllAgents(circuitcode);
        }

        public void NotifyMyCoarseLocationChange()
        {
            ForEachScenePresence(delegate(ScenePresence presence) { presence.CoarseLocationChange(); });
        }

        #endregion

        #region Entities

        public void SendKillObject(uint localID)
        {
            SceneObjectPart part = GetSceneObjectPart(localID);
            if (part != null) // It is a prim
            {
                if (part.ParentGroup != null && !part.ParentGroup.IsDeleted) // Valid
                {
                    if (part.ParentGroup.RootPart != part) // Child part
                        return;
                }
            }
            Broadcast(delegate(IClientAPI client) { client.SendKillObject(m_regionHandle, localID); });
        }

        #endregion

        #region RegionComms

        /// <summary>
        /// Register the methods that should be invoked when this scene receives various incoming events
        /// </summary>
        public void RegisterCommsEvents()
        {
            m_sceneGridService.OnExpectUser += HandleNewUserConnection;
            m_sceneGridService.OnAvatarCrossingIntoRegion += AgentCrossing;
            m_sceneGridService.OnCloseAgentConnection += IncomingCloseAgent;
            m_sceneGridService.OnRegionUp += OtherRegionUp;
            //m_sceneGridService.OnChildAgentUpdate += IncomingChildAgentDataUpdate;
            m_sceneGridService.OnExpectPrim += IncomingInterRegionPrimGroup;
            //m_sceneGridService.OnRemoveKnownRegionFromAvatar += HandleRemoveKnownRegionsFromAvatar;
            m_sceneGridService.OnLogOffUser += HandleLogOffUserFromGrid;
            m_sceneGridService.KiPrimitive += SendKillObject;
            m_sceneGridService.OnGetLandData += GetLandData;

            if (m_interregionCommsIn != null)
            {
                m_log.Debug("[SCENE]: Registering with InterregionCommsIn");
                m_interregionCommsIn.OnChildAgentUpdate += IncomingChildAgentDataUpdate;
            }
            else
                m_log.Debug("[SCENE]: Unable to register with InterregionCommsIn");

        }

        /// <summary>
        /// Deregister this scene from receiving incoming region events
        /// </summary>
        public void UnRegisterRegionWithComms()
        {
            m_sceneGridService.KiPrimitive -= SendKillObject;
            m_sceneGridService.OnLogOffUser -= HandleLogOffUserFromGrid;
            //m_sceneGridService.OnRemoveKnownRegionFromAvatar -= HandleRemoveKnownRegionsFromAvatar;
            m_sceneGridService.OnExpectPrim -= IncomingInterRegionPrimGroup;
            //m_sceneGridService.OnChildAgentUpdate -= IncomingChildAgentDataUpdate;
            m_sceneGridService.OnRegionUp -= OtherRegionUp;
            m_sceneGridService.OnExpectUser -= HandleNewUserConnection;
            m_sceneGridService.OnAvatarCrossingIntoRegion -= AgentCrossing;
            m_sceneGridService.OnCloseAgentConnection -= IncomingCloseAgent;
            m_sceneGridService.OnGetLandData -= GetLandData;

            if (m_interregionCommsIn != null)
                m_interregionCommsIn.OnChildAgentUpdate -= IncomingChildAgentDataUpdate;

            m_sceneGridService.Close();
        }

        /// <summary>
        /// A handler for the SceneCommunicationService event, to match that events return type of void.
        /// Use NewUserConnection() directly if possible so the return type can refuse connections.
        /// At the moment nothing actually seems to use this event,
        /// as everything is switching to calling the NewUserConnection method directly.
        /// </summary>
        /// <param name="agent"></param>
        public void HandleNewUserConnection(AgentCircuitData agent)
        {
            string reason;
            NewUserConnection(agent, out reason);
        }

        /// <summary>
        /// Do the work necessary to initiate a new user connection for a particular scene.
        /// At the moment, this consists of setting up the caps infrastructure
        /// The return bool should allow for connections to be refused, but as not all calling paths
        /// take proper notice of it let, we allowed banned users in still.
        /// </summary>
        /// <param name="regionHandle"></param>
        /// <param name="agent"></param>
        /// <param name="reason"></param>
        public bool NewUserConnection(AgentCircuitData agent, out string reason)
        {
            // Don't disable this log message - it's too helpful
            m_log.InfoFormat(
                "[CONNECTION BEGIN]: Region {0} told of incoming {1} agent {2} {3} {4} (circuit code {5})",
                RegionInfo.RegionName, (agent.child ? "child" : "root"), agent.firstname, agent.lastname, 
                agent.AgentID, agent.circuitcode);

            reason = String.Empty;
            if (!AuthenticateUser(agent, out reason))
                return false;

            if (!AuthorizeUser(agent, out reason))
                return false;

            m_log.InfoFormat(
                "[CONNECTION BEGIN]: Region {0} authenticated and authorized incoming {1} agent {2} {3} {4} (circuit code {5})",
                RegionInfo.RegionName, (agent.child ? "child" : "root"), agent.firstname, agent.lastname, 
                agent.AgentID, agent.circuitcode);

            CapsModule.NewUserConnection(agent);

            ScenePresence sp = m_sceneGraph.GetScenePresence(agent.AgentID);
            if (sp != null)
            {
                m_log.DebugFormat(
                    "[SCENE]: Adjusting known seeds for existing agent {0} in {1}",
                    agent.AgentID, RegionInfo.RegionName);
                
                sp.AdjustKnownSeeds();
                
                return true;
            }
            
            CapsModule.AddCapsHandler(agent.AgentID);
            
            if (!agent.child)
            {
                // Honor parcel landing type and position.
                ILandObject land = LandChannel.GetLandObject(agent.startpos.X, agent.startpos.Y);
                if (land != null)
                {
                    if (land.landData.LandingType == (byte)1 && land.landData.UserLocation != Vector3.Zero)
                    {
                        agent.startpos = land.landData.UserLocation;
                    }
                }
            }
            
            m_authenticateHandler.AddNewCircuit(agent.circuitcode, agent);
            
            // rewrite session_id
            CachedUserInfo userinfo = CommsManager.UserProfileCacheService.GetUserDetails(agent.AgentID);
            if (userinfo != null)
            {
                userinfo.SessionID = agent.SessionID;
            }
            else
            {
                m_log.WarnFormat(
                    "[CONNECTION BEGIN]: We couldn't find a User Info record for {0}.  This is usually an indication that the UUID we're looking up is invalid", agent.AgentID);
            }
            
            return true;
        }

        public virtual bool AuthenticateUser(AgentCircuitData agent, out string reason)
        {
            reason = String.Empty;

            bool result = CommsManager.UserService.VerifySession(agent.AgentID, agent.SessionID);
            m_log.Debug("[CONNECTION BEGIN]: User authentication returned " + result);
            if (!result) 
                reason = String.Format("Failed to authenticate user {0} {1}, access denied.", agent.firstname, agent.lastname);

            return result;
        }

        protected virtual bool AuthorizeUser(AgentCircuitData agent, out string reason)
        {
            reason = String.Empty;

            if (!m_strictAccessControl) return true;
            if (Permissions.IsGod(agent.AgentID)) return true;
            

            if (m_regInfo.EstateSettings.IsBanned(agent.AgentID))
            {
                m_log.WarnFormat("[CONNECTION BEGIN]: Denied access to: {0} ({1} {2}) at {3} because the user is on the banlist",
                                agent.AgentID, agent.firstname, agent.lastname, RegionInfo.RegionName);
                reason = String.Format("Denied access to region {0}: You have been banned from that region.",
                                       RegionInfo.RegionName);
                return false;
            }

            if (!m_regInfo.EstateSettings.PublicAccess && 
                !m_regInfo.EstateSettings.HasAccess(agent.AgentID))
            {
                m_log.WarnFormat("[CONNECTION BEGIN]: Denied access to: {0} ({1} {2}) at {3} because the user does not have access to the estate",
                                 agent.AgentID, agent.firstname, agent.lastname, RegionInfo.RegionName);
                reason = String.Format("Denied access to private region {0}: You are not on the access list for that region.", 
                                       RegionInfo.RegionName);
                return false;
            }

            // TODO: estate/region settings are not properly hooked up
            // to ILandObject.isRestrictedFromLand()
            // if (null != LandChannel)
            // {
            //     // region seems to have local Id of 1
            //     ILandObject land = LandChannel.GetLandObject(1);
            //     if (null != land)
            //     {
            //         if (land.isBannedFromLand(agent.AgentID))
            //         {
            //             m_log.WarnFormat("[CONNECTION BEGIN]: Denied access to: {0} ({1} {2}) at {3} because the user has been banned from land",
            //                              agent.AgentID, agent.firstname, agent.lastname, RegionInfo.RegionName);
            //             reason = String.Format("Denied access to private region {0}: You are banned from that region.", 
            //                                    RegionInfo.RegionName);
            //             return false;
            //         }

            //         if (land.isRestrictedFromLand(agent.AgentID))
            //         {
            //             m_log.WarnFormat("[CONNECTION BEGIN]: Denied access to: {0} ({1} {2}) at {3} because the user does not have access to the region",
            //                              agent.AgentID, agent.firstname, agent.lastname, RegionInfo.RegionName);
            //             reason = String.Format("Denied access to private region {0}: You are not on the access list for that region.", 
            //                                    RegionInfo.RegionName);
            //             return false;
            //         }
            //     }
            // }

            return true;
        }

        public void UpdateCircuitData(AgentCircuitData data)
        {
            m_authenticateHandler.UpdateAgentData(data);
        }

        public bool ChangeCircuitCode(uint oldcc, uint newcc)
        {
            return m_authenticateHandler.TryChangeCiruitCode(oldcc, newcc);
        }

        public void HandleLogOffUserFromGrid(UUID AvatarID, UUID RegionSecret, string message)
        {
            ScenePresence loggingOffUser = null;
            loggingOffUser = GetScenePresence(AvatarID);
            if (loggingOffUser != null)
            {
                UUID localRegionSecret = UUID.Zero;
                bool parsedsecret = UUID.TryParse(m_regInfo.regionSecret, out localRegionSecret);

                // Region Secret is used here in case a new sessionid overwrites an old one on the user server.
                // Will update the user server in a few revisions to use it.

                if (RegionSecret == loggingOffUser.ControllingClient.SecureSessionId || (parsedsecret && RegionSecret == localRegionSecret))
                {
                    m_sceneGridService.SendCloseChildAgentConnections(loggingOffUser.UUID, new List<ulong>(loggingOffUser.KnownRegions.Keys));
                    loggingOffUser.ControllingClient.Kick(message);
                    // Give them a second to receive the message!
                    Thread.Sleep(1000);
                    loggingOffUser.ControllingClient.Close(true);
                }
                else
                {
                    m_log.Info("[USERLOGOFF]: System sending the LogOff user message failed to sucessfully authenticate");
                }
            }
            else
            {
                m_log.InfoFormat("[USERLOGOFF]: Got a logoff request for {0} but the user isn't here.  The user might already have been logged out", AvatarID.ToString());
            }
        }

        /// <summary>
        /// Triggered when an agent crosses into this sim.  Also happens on initial login.
        /// </summary>
        /// <param name="agentID"></param>
        /// <param name="position"></param>
        /// <param name="isFlying"></param>
        public virtual void AgentCrossing(UUID agentID, Vector3 position, bool isFlying)
        {
            ScenePresence presence;

            lock (m_scenePresences)
            {
                m_scenePresences.TryGetValue(agentID, out presence);
            }

            if (presence != null)
            {
                try
                {
                    presence.MakeRootAgent(position, isFlying);
                }
                catch (Exception e)
                {
                    m_log.ErrorFormat("[SCENE]: Unable to do agent crossing, exception {0}", e);
                }
            }
            else
            {
                m_log.ErrorFormat(
                    "[SCENE]: Could not find presence for agent {0} crossing into scene {1}",
                    agentID, RegionInfo.RegionName);
            }
        }

        public virtual bool IncomingChildAgentDataUpdate(AgentData cAgentData)
        {
//            m_log.DebugFormat(
//                "[SCENE]: Incoming child agent update for {0} in {1}", cAgentData.AgentID, RegionInfo.RegionName);

            // We have to wait until the viewer contacts this region after receiving EAC.
            // That calls AddNewClient, which finally creates the ScenePresence
            ScenePresence childAgentUpdate = WaitGetScenePresence(cAgentData.AgentID);
            if (childAgentUpdate != null)
            {
                childAgentUpdate.ChildAgentDataUpdate(cAgentData);
                return true;
            }

            return false;
        }

        public virtual bool IncomingChildAgentDataUpdate(AgentPosition cAgentData)
        {
            //m_log.Debug(" XXX Scene IncomingChildAgentDataUpdate POSITION in " + RegionInfo.RegionName);
            ScenePresence childAgentUpdate = GetScenePresence(cAgentData.AgentID);
            if (childAgentUpdate != null)
            {
                // I can't imagine *yet* why we would get an update if the agent is a root agent..
                // however to avoid a race condition crossing borders..
                if (childAgentUpdate.IsChildAgent)
                {
                    uint rRegionX = (uint)(cAgentData.RegionHandle >> 40);
                    uint rRegionY = (((uint)(cAgentData.RegionHandle)) >> 8);
                    uint tRegionX = RegionInfo.RegionLocX;
                    uint tRegionY = RegionInfo.RegionLocY;
                    //Send Data to ScenePresence
                    childAgentUpdate.ChildAgentDataUpdate(cAgentData, tRegionX, tRegionY, rRegionX, rRegionY);
                    // Not Implemented:
                    //TODO: Do we need to pass the message on to one of our neighbors?
                }

                return true;
            }

            return false;
        }

        protected virtual ScenePresence WaitGetScenePresence(UUID agentID)
        {
            int ntimes = 10;
            ScenePresence childAgentUpdate = null;
            while ((childAgentUpdate = GetScenePresence(agentID)) == null && (ntimes-- > 0))
                Thread.Sleep(1000);
            return childAgentUpdate;

        }

        public virtual bool IncomingRetrieveRootAgent(UUID id, out IAgentData agent)
        {
            agent = null;
            ScenePresence sp = GetScenePresence(id);
            if ((sp != null) && (!sp.IsChildAgent))
            {
                sp.IsChildAgent = true;
                return sp.CopyAgent(out agent);
            }

            return false;
        }

        public virtual bool IncomingReleaseAgent(UUID id)
        {
            return m_sceneGridService.ReleaseAgent(id);
        }

        public void SendReleaseAgent(ulong regionHandle, UUID id, string uri)
        {
            m_interregionCommsOut.SendReleaseAgent(regionHandle, id, uri);
        }

        /// <summary>
        /// Tell a single agent to disconnect from the region.
        /// </summary>
        /// <param name="regionHandle"></param>
        /// <param name="agentID"></param>
        public bool IncomingCloseAgent(UUID agentID)
        {
            //m_log.DebugFormat("[SCENE]: Processing incoming close agent for {0}", agentID);

            ScenePresence presence = m_sceneGraph.GetScenePresence(agentID);
            if (presence != null)
            {
                // Nothing is removed here, so down count it as such
                if (presence.IsChildAgent)
                {
                   m_sceneGraph.removeUserCount(false);
                }
                else
                {
                   m_sceneGraph.removeUserCount(true);
                }

                // Don't do this to root agents on logout, it's not nice for the viewer
                if (presence.IsChildAgent)
                {
                    // Tell a single agent to disconnect from the region.
                    IEventQueue eq = RequestModuleInterface<IEventQueue>();
                    if (eq != null)
                    {
                        eq.DisableSimulator(RegionInfo.RegionHandle, agentID);
                    }
                    else
                        presence.ControllingClient.SendShutdownConnectionNotice();
                }

                presence.ControllingClient.Close(true);
                return true;
            }

            // Agent not here
            return false;
        }

        /// <summary>
        /// Tell neighboring regions about this agent
        /// When the regions respond with a true value,
        /// tell the agents about the region.
        ///
        /// We have to tell the regions about the agents first otherwise it'll deny them access
        ///
        /// </summary>
        /// <param name="presence"></param>
        public void InformClientOfNeighbours(ScenePresence presence)
        {
            m_sceneGridService.EnableNeighbourChildAgents(presence, m_neighbours);
        }

        /// <summary>
        /// Tell a neighboring region about this agent
        /// </summary>
        /// <param name="presence"></param>
        /// <param name="region"></param>
        public void InformClientOfNeighbor(ScenePresence presence, RegionInfo region)
        {
            m_sceneGridService.EnableNeighbourChildAgents(presence, m_neighbours);
        }

        /// <summary>
        /// Requests information about this region from gridcomms
        /// </summary>
        /// <param name="regionHandle"></param>
        /// <returns></returns>
        public RegionInfo RequestNeighbouringRegionInfo(ulong regionHandle)
        {
            return m_sceneGridService.RequestNeighbouringRegionInfo(regionHandle);
        }

        /// <summary>
        /// Requests textures for map from minimum region to maximum region in world cordinates
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="minX"></param>
        /// <param name="minY"></param>
        /// <param name="maxX"></param>
        /// <param name="maxY"></param>
        public void RequestMapBlocks(IClientAPI remoteClient, int minX, int minY, int maxX, int maxY)
        {
            m_log.DebugFormat("[MAPBLOCK]: {0}-{1}, {2}-{3}", minX, minY, maxX, maxY);
            m_sceneGridService.RequestMapBlocks(remoteClient, minX, minY, maxX, maxY);
        }

        /// <summary>
        /// Tries to teleport agent to other region.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="regionName"></param>
        /// <param name="position"></param>
        /// <param name="lookAt"></param>
        /// <param name="teleportFlags"></param>
        public void RequestTeleportLocation(IClientAPI remoteClient, string regionName, Vector3 position,
                                            Vector3 lookat, uint teleportFlags)
        {
            RegionInfo regionInfo = m_sceneGridService.RequestClosestRegion(regionName);
            if (regionInfo == null)
            {
                // can't find the region: Tell viewer and abort
                remoteClient.SendTeleportFailed("The region '" + regionName + "' could not be found.");
                return;
            }

            RequestTeleportLocation(remoteClient, regionInfo.RegionHandle, position, lookat, teleportFlags);
        }

        /// <summary>
        /// Tries to teleport agent to other region.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="regionHandle"></param>
        /// <param name="position"></param>
        /// <param name="lookAt"></param>
        /// <param name="teleportFlags"></param>
        public void RequestTeleportLocation(IClientAPI remoteClient, ulong regionHandle, Vector3 position,
                                            Vector3 lookAt, uint teleportFlags)
        {
            ScenePresence sp = null;
            lock (m_scenePresences)
            {
                if (m_scenePresences.ContainsKey(remoteClient.AgentId))
                    sp = m_scenePresences[remoteClient.AgentId];
            }

            if (sp != null)
            {
                m_sceneGridService.RequestTeleportToLocation(sp, regionHandle,
                                                             position, lookAt, teleportFlags);
            }
        }

        /// <summary>
        /// Tries to teleport agent to landmark.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="regionHandle"></param>
        /// <param name="position"></param>
        public void RequestTeleportLandmark(IClientAPI remoteClient, UUID regionID, Vector3 position)
        {
            RegionInfo info = CommsManager.GridService.RequestNeighbourInfo(regionID);

            if (info == null)
            {
                // can't find the region: Tell viewer and abort
                remoteClient.SendTeleportFailed("The teleport destination could not be found.");
                return;
            }

            ScenePresence sp = null;
            lock (m_scenePresences)
            {
                if (m_scenePresences.ContainsKey(remoteClient.AgentId))
                    sp = m_scenePresences[remoteClient.AgentId];
            }
            if (sp != null)
            {
                m_sceneGridService.RequestTeleportToLocation(sp, info.RegionHandle,
                    position, Vector3.Zero, (uint)(TPFlags.SetLastToTarget | TPFlags.ViaLandmark));
            }
        }

        public void CrossAgentToNewRegion(ScenePresence agent, bool isFlying)
        {
            m_sceneGridService.CrossAgentToNewRegion(this, agent, isFlying);
        }

        public void SendOutChildAgentUpdates(AgentPosition cadu, ScenePresence presence)
        {
            m_sceneGridService.SendChildAgentDataUpdate(cadu, presence);
        }

        #endregion

        #region Other Methods
        
        public void SetObjectCapacity(int objects)
        {
            // Region specific config overrides global
            //
            if (RegionInfo.ObjectCapacity != 0)
                objects = RegionInfo.ObjectCapacity;

            if (StatsReporter != null)
            {
                StatsReporter.SetObjectCapacity(objects);
            }
            objectCapacity = objects;
        }
        
        public List<FriendListItem> GetFriendList(string id)
        {
            UUID avatarID;
            if (!UUID.TryParse(id, out avatarID))
                return new List<FriendListItem>();

            return CommsManager.GetUserFriendList(avatarID);
        }

        public Dictionary<UUID, FriendRegionInfo> GetFriendRegionInfos(List<UUID> uuids)
        {
            return CommsManager.GetFriendRegionInfos(uuids);
        }

        public virtual void StoreAddFriendship(UUID ownerID, UUID friendID, uint perms)
        {
            m_sceneGridService.AddNewUserFriend(ownerID, friendID, perms);
        }

        public virtual void StoreUpdateFriendship(UUID ownerID, UUID friendID, uint perms)
        {
            m_sceneGridService.UpdateUserFriendPerms(ownerID, friendID, perms);
        }

        public virtual void StoreRemoveFriendship(UUID ownerID, UUID ExfriendID)
        {
            m_sceneGridService.RemoveUserFriend(ownerID, ExfriendID);
        }

        #endregion

        public void HandleObjectPermissionsUpdate(IClientAPI controller, UUID agentID, UUID sessionID, byte field, uint localId, uint mask, byte set)
        {
            // Check for spoofing..  since this is permissions we're talking about here!
            if ((controller.SessionId == sessionID) && (controller.AgentId == agentID))
            {
                // Tell the object to do permission update
                if (localId != 0)
                {
                    SceneObjectGroup chObjectGroup = GetGroupByPrim(localId);
                    if (chObjectGroup != null)
                    {
                        chObjectGroup.UpdatePermissions(agentID, field, localId, mask, set);
                    }
                }
            }
        }

        /// <summary>
        /// Causes all clients to get a full object update on all of the objects in the scene.
        /// </summary>
        public void ForceClientUpdate()
        {
            List<EntityBase> EntityList = GetEntities();

            foreach (EntityBase ent in EntityList)
            {
                if (ent is SceneObjectGroup)
                {
                    ((SceneObjectGroup)ent).ScheduleGroupForFullUpdate();
                }
            }
        }

        /// <summary>
        /// This is currently only used for scale (to scale to MegaPrim size)
        /// There is a console command that calls this in OpenSimMain
        /// </summary>
        /// <param name="cmdparams"></param>
        public void HandleEditCommand(string[] cmdparams)
        {
            m_log.Debug("Searching for Primitive: '" + cmdparams[2] + "'");

            List<EntityBase> EntityList = GetEntities();

            foreach (EntityBase ent in EntityList)
            {
                if (ent is SceneObjectGroup)
                {
                    SceneObjectPart part = ((SceneObjectGroup)ent).GetChildPart(((SceneObjectGroup)ent).UUID);
                    if (part != null)
                    {
                        if (part.Name == cmdparams[2])
                        {
                            part.Resize(
                                new Vector3(Convert.ToSingle(cmdparams[3]), Convert.ToSingle(cmdparams[4]),
                                              Convert.ToSingle(cmdparams[5])));

                            m_log.Debug("Edited scale of Primitive: " + part.Name);
                        }
                    }
                }
            }
        }

        public override void Show(string[] showParams)
        {
            base.Show(showParams);
            
            switch (showParams[0])
            {
                case "users":
                    m_log.Error("Current Region: " + RegionInfo.RegionName);
                    m_log.ErrorFormat("{0,-16}{1,-16}{2,-25}{3,-25}{4,-16}{5,-16}{6,-16}", "Firstname", "Lastname",
                                      "Agent ID", "Session ID", "Circuit", "IP", "World");

                    foreach (ScenePresence scenePresence in GetAvatars())
                    {
                        m_log.ErrorFormat("{0,-16}{1,-16}{2,-25}{3,-25}{4,-16},{5,-16}{6,-16}",
                                          scenePresence.Firstname,
                                          scenePresence.Lastname,
                                          scenePresence.UUID,
                                          scenePresence.ControllingClient.AgentId,
                                          "Unknown",
                                          "Unknown",
                                          RegionInfo.RegionName);
                    }
                
                    break;
            }           
        }

        #region Script Handling Methods

        /// <summary>
        /// Console command handler to send script command to script engine.
        /// </summary>
        /// <param name="args"></param>
        public void SendCommandToPlugins(string[] args)
        {
            m_eventManager.TriggerOnPluginConsole(args);
        }

        public LandData GetLandData(float x, float y)
        {
            return LandChannel.GetLandObject(x, y).landData;
        }

        public LandData GetLandData(uint x, uint y)
        {
            m_log.DebugFormat("[SCENE]: returning land for {0},{1}", x, y);
            return LandChannel.GetLandObject((int)x, (int)y).landData;
        }

        public RegionInfo RequestClosestRegion(string name)
        {
            return m_sceneGridService.RequestClosestRegion(name);
        }

        #endregion

        #region Script Engine

        private List<ScriptEngineInterface> ScriptEngines = new List<ScriptEngineInterface>();
        public bool DumpAssetsToFile;

        /// <summary>
        ///
        /// </summary>
        /// <param name="scriptEngine"></param>
        public void AddScriptEngine(ScriptEngineInterface scriptEngine)
        {
            ScriptEngines.Add(scriptEngine);
            scriptEngine.InitializeEngine(this);
        }

        private bool ScriptDanger(SceneObjectPart part,Vector3 pos)
        {
            ILandObject parcel = LandChannel.GetLandObject(pos.X, pos.Y);
            if (part != null)
            {
                if (parcel != null)
                {
                    if ((parcel.landData.Flags & (uint)Parcel.ParcelFlags.AllowOtherScripts) != 0)
                    {
                        return true;
                    }
                    else if ((parcel.landData.Flags & (uint)Parcel.ParcelFlags.AllowGroupScripts) != 0)
                    {
                        if (part.OwnerID == parcel.landData.OwnerID 
                            || (parcel.landData.IsGroupOwned && part.GroupID == parcel.landData.GroupID) 
                            || Permissions.IsGod(part.OwnerID))
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (part.OwnerID == parcel.landData.OwnerID)
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
                else
                {

                    if (pos.X > 0f && pos.X < Constants.RegionSize && pos.Y > 0f && pos.Y < Constants.RegionSize)
                    {
                        // The only time parcel != null when an object is inside a region is when
                        // there is nothing behind the landchannel.  IE, no land plugin loaded.
                        return true;
                    }
                    else
                    {
                        // The object is outside of this region.  Stop piping events to it.
                        return false;
                    }
                }
            }
            else
            {
                return false;
            }
        }

        public bool ScriptDanger(uint localID, Vector3 pos)
        {
            SceneObjectPart part = GetSceneObjectPart(localID);
            if (part != null)
            {
                return ScriptDanger(part, pos);
            }
            else
            {
                return false;
            }
        }

        public bool PipeEventsForScript(uint localID)
        {
            SceneObjectPart part = GetSceneObjectPart(localID);
            if (part != null)
            {
                // Changed so that child prims of attachments return ScriptDanger for their parent, so that
                //  their scripts will actually run.
                //      -- Leaf, Tue Aug 12 14:17:05 EDT 2008
                SceneObjectPart parent = part.ParentGroup.RootPart;
                if (parent != null && parent.IsAttachment)
                    return ScriptDanger(parent, parent.GetWorldPosition());
                else
                    return ScriptDanger(part, part.GetWorldPosition());
            }
            else
            {
                return false;
            }
        }

        #endregion

        #region SceneGraph wrapper methods

        /// <summary>
        ///
        /// </summary>
        /// <param name="localID"></param>
        /// <returns></returns>
        public UUID ConvertLocalIDToFullID(uint localID)
        {
            return m_sceneGraph.ConvertLocalIDToFullID(localID);
        }

        public void SwapRootAgentCount(bool rootChildChildRootTF)
        {
            m_sceneGraph.SwapRootChildAgent(rootChildChildRootTF);
        }

        public void AddPhysicalPrim(int num)
        {
            m_sceneGraph.AddPhysicalPrim(num);
        }

        public void RemovePhysicalPrim(int num)
        {
            m_sceneGraph.RemovePhysicalPrim(num);
        }

        //The idea is to have a group of method that return a list of avatars meeting some requirement
        // ie it could be all m_scenePresences within a certain range of the calling prim/avatar.

        /// <summary>
        /// Return a list of all avatars in this region.
        /// This list is a new object, so it can be iterated over without locking.
        /// </summary>
        /// <returns></returns>
        public List<ScenePresence> GetAvatars()
        {
            return m_sceneGraph.GetAvatars();
        }

        /// <summary>
        /// Return a list of all ScenePresences in this region.  This returns child agents as well as root agents.
        /// This list is a new object, so it can be iterated over without locking.
        /// </summary>
        /// <returns></returns>
        public List<ScenePresence> GetScenePresences()
        {
            return m_sceneGraph.GetScenePresences();
        }

        /// <summary>
        /// Request a filtered list of ScenePresences in this region.
        /// This list is a new object, so it can be iterated over without locking.
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        public List<ScenePresence> GetScenePresences(FilterAvatarList filter)
        {
            return m_sceneGraph.GetScenePresences(filter);
        }

        /// <summary>
        /// Request a scene presence by UUID
        /// </summary>
        /// <param name="avatarID"></param>
        /// <returns></returns>
        public ScenePresence GetScenePresence(UUID avatarID)
        {
            return m_sceneGraph.GetScenePresence(avatarID);
        }

        public override bool PresenceChildStatus(UUID avatarID)
        {
            ScenePresence cp = GetScenePresence(avatarID);

            // FIXME: This is really crap - some logout code is relying on a NullReferenceException to halt its processing
            // This needs to be fixed properly by cleaning up the logout code.
            //if (cp != null)
            //    return cp.IsChildAgent;

            //return false;

            return cp.IsChildAgent;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="action"></param>
        public void ForEachScenePresence(Action<ScenePresence> action)
        {
            // We don't want to try to send messages if there are no avatars.
            if (m_scenePresences != null)
            {
                try
                {
                    List<ScenePresence> presenceList = GetScenePresences();
                    foreach (ScenePresence presence in presenceList)
                    {
                        action(presence);
                    }
                }
                catch (Exception e)
                {
                    m_log.Info("[BUG] in " + RegionInfo.RegionName + ": " + e.ToString());
                    m_log.Info("[BUG] Stack Trace: " + e.StackTrace);
                }
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="action"></param>
        //        public void ForEachObject(Action<SceneObjectGroup> action)
        //        {
        //            List<SceneObjectGroup> presenceList;
        //
        //            lock (m_sceneObjects)
        //            {
        //                presenceList = new List<SceneObjectGroup>(m_sceneObjects.Values);
        //            }
        //
        //            foreach (SceneObjectGroup presence in presenceList)
        //            {
        //                action(presence);
        //            }
        //        }

        /// <summary>
        /// Get a named prim contained in this scene (will return the first
        /// found, if there are more than one prim with the same name)
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public SceneObjectPart GetSceneObjectPart(string name)
        {
            return m_sceneGraph.GetSceneObjectPart(name);
        }

        /// <summary>
        /// Get a prim via its local id
        /// </summary>
        /// <param name="localID"></param>
        /// <returns></returns>
        public SceneObjectPart GetSceneObjectPart(uint localID)
        {
            return m_sceneGraph.GetSceneObjectPart(localID);
        }

        /// <summary>
        /// Get a prim via its UUID
        /// </summary>
        /// <param name="fullID"></param>
        /// <returns></returns>
        public SceneObjectPart GetSceneObjectPart(UUID fullID)
        {
            return m_sceneGraph.GetSceneObjectPart(fullID);
        }

        public bool TryGetAvatar(UUID avatarId, out ScenePresence avatar)
        {
            return m_sceneGraph.TryGetAvatar(avatarId, out avatar);
        }

        public bool TryGetAvatarByName(string avatarName, out ScenePresence avatar)
        {
            return m_sceneGraph.TryGetAvatarByName(avatarName, out avatar);
        }

        public void ForEachClient(Action<IClientAPI> action)
        {
            m_sceneGraph.ForEachClient(action);
        }

        /// <summary>
        /// Returns a list of the entities in the scene.  This is a new list so operations perform on the list itself
        /// will not affect the original list of objects in the scene.
        /// </summary>
        /// <returns></returns>
        public List<EntityBase> GetEntities()
        {
            return m_sceneGraph.GetEntities();
        }

        #endregion

        #region Avatar Appearance Default

        public static void GetDefaultAvatarAppearance(out AvatarWearable[] wearables, out byte[] visualParams)
        {
            visualParams = AvatarAppearance.GetDefaultVisualParams();
            wearables = AvatarWearable.DefaultWearables;
        }

        #endregion

        public void RegionHandleRequest(IClientAPI client, UUID regionID)
        {
            RegionInfo info;
            if (regionID == RegionInfo.RegionID)
                info = RegionInfo;
            else
                info = CommsManager.GridService.RequestNeighbourInfo(regionID);

            if (info != null)
                client.SendRegionHandle(regionID, info.RegionHandle);
        }

        public void TerrainUnAcked(IClientAPI client, int patchX, int patchY)
        {
            //m_log.Debug("Terrain packet unacked, resending patch: " + patchX + " , " + patchY);
             client.SendLayerData(patchX, patchY, Heightmap.GetFloatsSerialized());
        }

        public void SetRootAgentScene(UUID agentID)
        {
            IInventoryTransferModule inv = RequestModuleInterface<IInventoryTransferModule>();
            if (inv == null)
                return;

            inv.SetRootAgentScene(agentID, this);

            EventManager.TriggerSetRootAgentScene(agentID, this);
        }

        public bool NeedSceneCacheClear(UUID agentID)
        {
            IInventoryTransferModule inv = RequestModuleInterface<IInventoryTransferModule>();
            if (inv == null)
                return true;

            return inv.NeedSceneCacheClear(agentID, this);
        }

        public void ObjectSaleInfo(IClientAPI client, UUID agentID, UUID sessionID, uint localID, byte saleType, int salePrice)
        {
            SceneObjectPart part = GetSceneObjectPart(localID);
            if (part == null || part.ParentGroup == null)
                return;

            if (part.ParentGroup.IsDeleted)
                return;

            part = part.ParentGroup.RootPart;

            part.ObjectSaleType = saleType;
            part.SalePrice = salePrice;

            part.ParentGroup.HasGroupChanged = true;

            part.GetProperties(client);
        }

        public bool PerformObjectBuy(IClientAPI remoteClient, UUID categoryID,
                uint localID, byte saleType)
        {
            SceneObjectPart part = GetSceneObjectPart(localID);

            if (part == null)
                return false;

            if (part.ParentGroup == null)
                return false;

            SceneObjectGroup group = part.ParentGroup;

            switch (saleType)
            {
            case 1: // Sell as original (in-place sale)
                uint effectivePerms=group.GetEffectivePermissions();

                if ((effectivePerms & (uint)PermissionMask.Transfer) == 0)
                {
                    m_dialogModule.SendAlertToUser(remoteClient, "This item doesn't appear to be for sale");
                    return false;
                }

                group.SetOwnerId(remoteClient.AgentId);
                group.SetRootPartOwner(part, remoteClient.AgentId,
                        remoteClient.ActiveGroupId);

                List<SceneObjectPart> partList =
                    new List<SceneObjectPart>(group.Children.Values);

                if (Permissions.PropagatePermissions())
                {
                    foreach (SceneObjectPart child in partList)
                    {
                        child.Inventory.ChangeInventoryOwner(remoteClient.AgentId);
                        child.ApplyNextOwnerPermissions();
                    }
                }

                part.ObjectSaleType = 0;
                part.SalePrice = 10;

                group.HasGroupChanged = true;
                part.GetProperties(remoteClient);
                part.ScheduleFullUpdate();

                break;

            case 2: // Sell a copy
                string sceneObjectXml = SceneObjectSerializer.ToOriginalXmlFormat(group);

                CachedUserInfo userInfo =
                    CommsManager.UserProfileCacheService.GetUserDetails(remoteClient.AgentId);

                if (userInfo != null)
                {
                    uint perms=group.GetEffectivePermissions();

                    if ((perms & (uint)PermissionMask.Transfer) == 0)
                    {
                        m_dialogModule.SendAlertToUser(remoteClient, "This item doesn't appear to be for sale");
                        return false;
                    }

                    AssetBase asset = CreateAsset(
                        group.GetPartName(localID),
                        group.GetPartDescription(localID),
                        (sbyte)AssetType.Object,
                        Utils.StringToBytes(sceneObjectXml));
                    CommsManager.AssetCache.AddAsset(asset);

                    InventoryItemBase item = new InventoryItemBase();
                    item.CreatorId = part.CreatorID.ToString();

                    item.ID = UUID.Random();
                    item.Owner = remoteClient.AgentId;
                    item.AssetID = asset.FullID;
                    item.Description = asset.Description;
                    item.Name = asset.Name;
                    item.AssetType = asset.Type;
                    item.InvType = (int)InventoryType.Object;
                    item.Folder = categoryID;

                    uint nextPerms=(perms & 7) << 13;
                    if ((nextPerms & (uint)PermissionMask.Copy) == 0)
                        perms &= ~(uint)PermissionMask.Copy;
                    if ((nextPerms & (uint)PermissionMask.Transfer) == 0)
                        perms &= ~(uint)PermissionMask.Transfer;
                    if ((nextPerms & (uint)PermissionMask.Modify) == 0)
                        perms &= ~(uint)PermissionMask.Modify;

                    item.BasePermissions = perms & part.NextOwnerMask;
                    item.CurrentPermissions = perms & part.NextOwnerMask;
                    item.NextPermissions = part.NextOwnerMask;
                    item.EveryOnePermissions = part.EveryoneMask &
                                               part.NextOwnerMask;
                    item.GroupPermissions = part.GroupMask &
                                               part.NextOwnerMask;
                    item.CurrentPermissions |= 8; // Slam!
                    item.CreationDate = Util.UnixTimeSinceEpoch();

                    userInfo.AddItem(item);
                    remoteClient.SendInventoryItemCreateUpdate(item, 0);
                }
                else
                {
                    m_dialogModule.SendAlertToUser(remoteClient, "Cannot buy now. Your inventory is unavailable");
                    return false;
                }
                break;

            case 3: // Sell contents
                List<UUID> invList = part.Inventory.GetInventoryList();

                bool okToSell = true;

                foreach (UUID invID in invList)
                {
                    TaskInventoryItem item = part.Inventory.GetInventoryItem(invID);
                    if ((item.CurrentPermissions &
                            (uint)PermissionMask.Transfer) == 0)
                    {
                        okToSell = false;
                        break;
                    }
                }

                if (!okToSell)
                {
                    m_dialogModule.SendAlertToUser(
                        remoteClient, "This item's inventory doesn't appear to be for sale");
                    return false;
                }

                if (invList.Count > 0)
                    MoveTaskInventoryItems(remoteClient.AgentId, part.Name,
                            part, invList);
                break;
            }

            return true;
        }

        public void CleanTempObjects()
        {
            List<EntityBase> objs = GetEntities();

            foreach (EntityBase obj in objs)
            {
                if (obj is SceneObjectGroup)
                {
                    SceneObjectGroup grp = (SceneObjectGroup)obj;

                    if (!grp.IsDeleted)
                    {
                        if ((grp.RootPart.Flags & PrimFlags.TemporaryOnRez) != 0)
                        {
                            if (grp.RootPart.Expires <= DateTime.Now)
                                DeleteSceneObject(grp, false);
                        }
                    }
                }
            }
        }

        public void DeleteFromStorage(UUID uuid)
        {
            m_storageManager.DataStore.RemoveObject(uuid, m_regInfo.RegionID);
        }

        public int GetHealth()
        {
            // Returns:
            // 1 = sim is up and accepting http requests. The heartbeat has
            // stopped and the sim is probably locked up, but a remote
            // admin restart may succeed
            //
            // 2 = Sim is up and the heartbeat is running. The sim is likely
            // usable for people within and logins _may_ work
            //
            // 3 = We have seen a new user enter within the past 4 minutes
            // which can be seen as positive confirmation of sim health
            //
            int health=1; // Start at 1, means we're up

            if ((Environment.TickCount - m_lastUpdate) < 1000)
                health+=1;
            else
                return health;

            // A login in the last 4 mins? We can't be doing too badly
            //
            if ((Environment.TickCount - m_LastLogin) < 240000)
                health++;
            else
                return health;

            return health;
        }

        // This callback allows the PhysicsScene to call back to its caller (the SceneGraph) and
        // update non-physical objects like the joint proxy objects that represent the position
        // of the joints in the scene.

        // This routine is normally called from within a lock (OdeLock) from within the OdePhysicsScene
        // WARNING: be careful of deadlocks here if you manipulate the scene. Remember you are being called
        // from within the OdePhysicsScene.

        protected internal void jointMoved(PhysicsJoint joint)
        {
            // m_parentScene.PhysicsScene.DumpJointInfo(); // non-thread-locked version; we should already be in a lock (OdeLock) when this callback is invoked
            SceneObjectPart jointProxyObject = GetSceneObjectPart(joint.ObjectNameInScene);
            if (jointProxyObject == null)
            {
                jointErrorMessage(joint, "WARNING, joint proxy not found, name " + joint.ObjectNameInScene);
                return;
            }

            // now update the joint proxy object in the scene to have the position of the joint as returned by the physics engine
            SceneObjectPart trackedBody = GetSceneObjectPart(joint.TrackedBodyName); // FIXME: causes a sequential lookup
            if (trackedBody == null) return; // the actor may have been deleted but the joint still lingers around a few frames waiting for deletion. during this time, trackedBody is NULL to prevent further motion of the joint proxy.
            jointProxyObject.Velocity = trackedBody.Velocity;
            jointProxyObject.RotationalVelocity = trackedBody.RotationalVelocity;
            switch (joint.Type)
            {
                case PhysicsJointType.Ball:
                    {
                        PhysicsVector jointAnchor = PhysicsScene.GetJointAnchor(joint);
                        Vector3 proxyPos = new Vector3(jointAnchor.X, jointAnchor.Y, jointAnchor.Z);
                        jointProxyObject.ParentGroup.UpdateGroupPosition(proxyPos); // schedules the entire group for a terse update
                    }
                    break;

                case PhysicsJointType.Hinge:
                    {
                        PhysicsVector jointAnchor = PhysicsScene.GetJointAnchor(joint);

                        // Normally, we would just ask the physics scene to return the axis for the joint.
                        // Unfortunately, ODE sometimes returns <0,0,0> for the joint axis, which should
                        // never occur. Therefore we cannot rely on ODE to always return a correct joint axis.
                        // Therefore the following call does not always work:
                        //PhysicsVector phyJointAxis = _PhyScene.GetJointAxis(joint);

                        // instead we compute the joint orientation by saving the original joint orientation
                        // relative to one of the jointed bodies, and applying this transformation
                        // to the current position of the jointed bodies (the tracked body) to compute the
                        // current joint orientation.

                        if (joint.TrackedBodyName == null)
                        {
                            jointErrorMessage(joint, "joint.TrackedBodyName is null, joint " + joint.ObjectNameInScene);
                        }

                        Vector3 proxyPos = new Vector3(jointAnchor.X, jointAnchor.Y, jointAnchor.Z);
                        Quaternion q = trackedBody.RotationOffset * joint.LocalRotation;

                        jointProxyObject.ParentGroup.UpdateGroupPosition(proxyPos); // schedules the entire group for a terse update
                        jointProxyObject.ParentGroup.UpdateGroupRotation(q); // schedules the entire group for a terse update
                    }
                    break;
            }
        }

        // This callback allows the PhysicsScene to call back to its caller (the SceneGraph) and
        // update non-physical objects like the joint proxy objects that represent the position
        // of the joints in the scene.

        // This routine is normally called from within a lock (OdeLock) from within the OdePhysicsScene
        // WARNING: be careful of deadlocks here if you manipulate the scene. Remember you are being called
        // from within the OdePhysicsScene.
        protected internal void jointDeactivated(PhysicsJoint joint)
        {
            //m_log.Debug("[NINJA] SceneGraph.jointDeactivated, joint:" + joint.ObjectNameInScene);
            SceneObjectPart jointProxyObject = GetSceneObjectPart(joint.ObjectNameInScene);
            if (jointProxyObject == null)
            {
                jointErrorMessage(joint, "WARNING, trying to deactivate (stop interpolation of) joint proxy, but not found, name " + joint.ObjectNameInScene);
                return;
            }

            // turn the proxy non-physical, which also stops its client-side interpolation
            bool wasUsingPhysics = ((jointProxyObject.ObjectFlags & (uint)PrimFlags.Physics) != 0);
            if (wasUsingPhysics)
            {
                jointProxyObject.UpdatePrimFlags(false, false, true, false); // FIXME: possible deadlock here; check to make sure all the scene alterations set into motion here won't deadlock
            }
        }

        // This callback allows the PhysicsScene to call back to its caller (the SceneGraph) and
        // alert the user of errors by using the debug channel in the same way that scripts alert
        // the user of compile errors.

        // This routine is normally called from within a lock (OdeLock) from within the OdePhysicsScene
        // WARNING: be careful of deadlocks here if you manipulate the scene. Remember you are being called
        // from within the OdePhysicsScene.
        public void jointErrorMessage(PhysicsJoint joint, string message)
        {
            if (joint != null)
            {
                if (joint.ErrorMessageCount > PhysicsJoint.maxErrorMessages)
                    return;

                SceneObjectPart jointProxyObject = GetSceneObjectPart(joint.ObjectNameInScene);
                if (jointProxyObject != null)
                {
                    SimChat(Utils.StringToBytes("[NINJA]: " + message),
                        ChatTypeEnum.DebugChannel,
                        2147483647,
                        jointProxyObject.AbsolutePosition,
                        jointProxyObject.Name,
                        jointProxyObject.UUID,
                        false);

                    joint.ErrorMessageCount++;

                    if (joint.ErrorMessageCount > PhysicsJoint.maxErrorMessages)
                    {
                        SimChat(Utils.StringToBytes("[NINJA]: Too many messages for this joint, suppressing further messages."),
                            ChatTypeEnum.DebugChannel,
                            2147483647,
                            jointProxyObject.AbsolutePosition,
                            jointProxyObject.Name,
                            jointProxyObject.UUID,
                            false);
                    }
                }
                else
                {
                    // couldn't find the joint proxy object; the error message is silently suppressed
                }
            }
        }

        public Scene ConsoleScene()
        {
            if (MainConsole.Instance == null)
                return null;
            if (MainConsole.Instance.ConsoleScene is Scene)
                return (Scene)MainConsole.Instance.ConsoleScene;
            return null;
        }
    }
}
