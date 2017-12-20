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
using System.Net;
using System.Collections.Generic;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Communications;
using OpenSim.Framework.Communications.Cache;
using OpenSim.Framework.Servers;
using OpenSim.Region.Physics.Manager;
using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.CoreModules.Agent.Capabilities;
using OpenSim.Region.CoreModules.Avatar.Gods;
using OpenSim.Tests.Common.Mock;

namespace OpenSim.Tests.Common.Setup
{
    /// <summary>
    /// Helpers for setting up scenes.
    /// </summary>
    public class SceneSetupHelpers
    {
        /// <summary>
        /// Set up a test scene
        /// </summary>
        /// 
        /// Automatically starts service threads, as would the normal runtime.
        /// 
        /// <returns></returns>
        public static TestScene SetupScene()
        {
            return SetupScene(true);
        }
        
        /// <summary>
        /// Set up a test scene
        /// </summary>
        /// 
        /// <param name="startServices">Start associated service threads for the scene</param>
        /// <returns></returns>
        public static TestScene SetupScene(bool startServices)
        {
            return SetupScene(
                "Unit test region", UUID.Random(), 1000, 1000, new TestCommunicationsManager(), startServices);
        }
        
        /// <summary>
        /// Set up a test scene
        /// </summary>
        /// <param name="name">Name of the region</param>
        /// <param name="id">ID of the region</param>
        /// <param name="x">X co-ordinate of the region</param>
        /// <param name="y">Y co-ordinate of the region</param>
        /// <param name="cm">This should be the same if simulating two scenes within a standalone</param>
        /// <returns></returns>
        public static TestScene SetupScene(string name, UUID id, uint x, uint y, TestCommunicationsManager cm)
        {
            return SetupScene(name, id, x, y, cm, true);
        }

        /// <summary>
        /// Set up a test scene
        /// </summary>
        /// <param name="name">Name of the region</param>
        /// <param name="id">ID of the region</param>
        /// <param name="x">X co-ordinate of the region</param>
        /// <param name="y">Y co-ordinate of the region</param>
        /// <param name="cm">This should be the same if simulating two scenes within a standalone</param>
        /// <param name="startServices">Start associated threads for the services used by the scene</param>
        /// <returns></returns>
        public static TestScene SetupScene(
            string name, UUID id, uint x, uint y, TestCommunicationsManager cm, bool startServices)
        {
            Console.WriteLine("Setting up test scene {0}", name);
            
            RegionInfo regInfo = new RegionInfo(x, y, new IPEndPoint(IPAddress.Loopback, 9000), "127.0.0.1");
            regInfo.RegionName = name;
            regInfo.RegionID = id;

            AgentCircuitManager acm = new AgentCircuitManager();
            SceneCommunicationService scs = new SceneCommunicationService(cm);

            StorageManager sm = new StorageManager("OpenSim.Data.Null.dll", "", "");
            IConfigSource configSource = new IniConfigSource();

            TestScene testScene = new TestScene(
                regInfo, acm, cm, scs, sm, null, false, false, false, configSource, null);

            IRegionModule capsModule = new CapabilitiesModule();            
            capsModule.Initialize(testScene, new IniConfigSource());
            testScene.AddModule(capsModule.Name, capsModule);
            
            IRegionModule godsModule = new GodsModule();
            godsModule.Initialize(testScene, new IniConfigSource());
            testScene.AddModule(godsModule.Name, godsModule);
            
            testScene.SetModuleInterfaces();

            testScene.LandChannel = new TestLandChannel();
            testScene.LoadWorldMap();

            PhysicsPluginManager physicsPluginManager = new PhysicsPluginManager();
            physicsPluginManager.LoadPluginsFromAssembly("Physics/OpenSim.Region.Physics.BasicPhysicsPlugin.dll");
            testScene.PhysicsScene
                = physicsPluginManager.GetPhysicsScene("basicphysics", "ZeroMesher", configSource, "test");

            if (startServices)
                cm.StartServices();
            
            return testScene;
        }

        /// <summary>
        /// Setup modules for a scene using their default settings.
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="modules"></param>
        public static void SetupSceneModules(Scene scene, params object[] modules)
        {
            SetupSceneModules(scene, null, modules);
        }

        /// <summary>
        /// Setup modules for a scene.
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="config"></param>
        /// <param name="modules"></param>
        public static void SetupSceneModules(Scene scene, IConfigSource config, params object[] modules)
        {
            List<IRegionModuleBase> newModules = new List<IRegionModuleBase>();
            foreach (object module in modules)
            {
                if (module is IRegionModule)
                {
                    IRegionModule m = (IRegionModule)module;
                    m.Initialize(scene, config);
                    scene.AddModule(m.Name, m);
                }
                else if (module is IRegionModuleBase)
                {
                    // for the new system, everything has to be initialized first,
                    // shared modules have to be post-initialized, then all get an AddRegion with the scene
                    IRegionModuleBase m = (IRegionModuleBase)module;
                    m.Initialize(config);
                    newModules.Add(m);
                }
            }

            foreach (IRegionModuleBase module in newModules)
            {
                if (module is ISharedRegionModule) ((ISharedRegionModule)module).PostInitialize();
            }

            foreach (IRegionModuleBase module in newModules)
            {
                module.AddRegion(scene);
                scene.AddRegionModule(module.Name, module);
            }

            scene.SetModuleInterfaces();
        }

        /// <summary>
        /// Generate some standard agent connection data.
        /// </summary>
        /// <param name="agentId"></param>
        /// <returns></returns>
        public static AgentCircuitData GenerateAgentData(UUID agentId)
        {
            string firstName = "testfirstname";

            AgentCircuitData agentData = new AgentCircuitData();
            agentData.AgentID = agentId;
            agentData.firstname = firstName;
            agentData.lastname = "testlastname";
            agentData.SessionID = UUID.Zero;
            agentData.SecureSessionID = UUID.Zero;
            agentData.circuitcode = 123;
            agentData.BaseFolder = UUID.Zero;
            agentData.InventoryFolder = UUID.Zero;
            agentData.startpos = Vector3.Zero;
            agentData.CapsPath = "http://wibble.com";

            return agentData;
        }

        /// <summary>
        /// Add a root agent where the details of the agent connection (apart from the id) are unimportant for the test
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="agentId"></param>
        /// <returns></returns>
        public static TestClient AddRootAgent(Scene scene, UUID agentId)
        {
            return AddRootAgent(scene, GenerateAgentData(agentId));
        }

        /// <summary>
        /// Add a root agent.
        /// </summary>
        ///
        /// This function
        ///
        /// 1)  Tells the scene that an agent is coming.  Normally, the login service (local if standalone, from the
        /// userserver if grid) would give initial login data back to the client and separately tell the scene that the
        /// agent was coming.
        ///
        /// 2)  Connects the agent with the scene
        ///
        /// This function performs actions equivalent with notifying the scene that an agent is
        /// coming and then actually connecting the agent to the scene.  The one step missed out is the very first
        ///
        /// <param name="scene"></param>
        /// <param name="agentData"></param>
        /// <returns></returns>
        public static TestClient AddRootAgent(Scene scene, AgentCircuitData agentData)
        {
            string reason;

            // We emulate the proper login sequence here by doing things in three stages
            // Stage 1: simulate login by telling the scene to expect a new user connection
            scene.NewUserConnection(agentData, out reason);

            // Stage 2: add the new client as a child agent to the scene
            TestClient client = new TestClient(agentData, scene);
            scene.AddNewClient(client);

            // Stage 3: Invoke agent crossing, which converts the child agent into a root agent (with appearance,
            // inventory, etc.)
            //scene.AgentCrossing(agentData.AgentID, new Vector3(90, 90, 90), false); OBSOLETE

            ScenePresence scp = scene.GetScenePresence(agentData.AgentID);
            scp.MakeRootAgent(new Vector3(90,90,90), true);

            return client;
        }

        /// <summary>
        /// Add a test object
        /// </summary>
        /// <param name="scene"></param>
        /// <returns></returns>
        public static SceneObjectPart AddSceneObject(Scene scene)
        {
            return AddSceneObject(scene, "Test Object");
        }

        /// <summary>
        /// Add a test object
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static SceneObjectPart AddSceneObject(Scene scene, string name)
        {
            SceneObjectPart part
                = new SceneObjectPart(UUID.Zero, PrimitiveBaseShape.Default, Vector3.Zero, Quaternion.Identity, Vector3.Zero);
            part.Name = name;

            //part.UpdatePrimFlags(false, false, true);
            //part.ObjectFlags |= (uint)PrimFlags.Phantom;

            scene.AddNewSceneObject(new SceneObjectGroup(part), false);

            return part;
        }

        /// <summary>
        /// Delete a scene object asynchronously
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="part"></param>
        /// <param name="action"></param>
        /// <param name="destinationId"></param>
        /// <param name="client"></param>
        public static void DeleteSceneObjectAsync(
            TestScene scene, SceneObjectPart part, DeRezAction action, UUID destinationId, IClientAPI client)
        {
            // Turn off the timer on the async sog deleter - we'll crank it by hand within a unit test
            AsyncSceneObjectGroupDeleter sogd = scene.SceneObjectGroupDeleter;
            sogd.Enabled = false;

            scene.DeRezObject(client, part.LocalId, UUID.Zero, action, destinationId);
            sogd.InventoryDeQueueAndDelete();
        }
    }
}
