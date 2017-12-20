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
using System.Collections.Generic;
using System.Reflection;
using System.Timers;
using OpenMetaverse;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.OptionalModules.World.TreePopulator
{
    /// <summary>
    /// Version 2.01 - Very hacky compared to the original. Will fix original and release as 0.3 later.
    /// </summary>
    public class TreePopulatorModule : IRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private Scene m_scene;

        public double m_tree_density = 50.0; // Aim for this many per region
        public double m_tree_updates = 1000.0; // MS between updates
        private bool m_active_trees = false;
        private List<UUID> m_trees;
        Timer CalculateTrees;

        #region IRegionModule Members

        public void Initialise(Scene scene, IConfigSource config)
        {
            m_scene = scene;
            m_scene.RegisterModuleInterface<IRegionModule>(this);

            m_scene.AddCommand(
                this, "tree plant", "tree plant", "Start populating trees", HandleTreeConsoleCommand);

            m_scene.AddCommand(
                this, "tree active", "tree active <boolean>", "Change activity state for trees module", HandleTreeConsoleCommand);

            try
            {
                m_tree_density = config.Configs["Trees"].GetDouble("tree_density", m_tree_density);
                m_active_trees = config.Configs["Trees"].GetBoolean("active_trees", m_active_trees);
            }
            catch (Exception)
            {
            }

            m_trees = new List<UUID>();

            if (m_active_trees)
                activeizeTreeze(true);

            m_log.Debug("[TREES]: Initialised tree module");
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "TreePopulatorModule"; }
        }

        public bool IsSharedModule
        {
            get { return false; }
        }

        #endregion

        /// <summary>
        /// Handle a tree command from the console.
        /// </summary>
        /// <param name="module"></param>
        /// <param name="cmdparams"></param>
        public void HandleTreeConsoleCommand(string module, string[] cmdparams)
        {
            if (m_scene.ConsoleScene() != null && m_scene.ConsoleScene() != m_scene)
                return;

            if (cmdparams[1] == "active")
            {
                if (cmdparams.Length <= 2)
                {
                    if (m_active_trees)
                        m_log.InfoFormat("[TREES]: Trees are currently active");
                    else
                        m_log.InfoFormat("[TREES]: Trees are currently not active");
                }
                else if (cmdparams[2] == "true" && !m_active_trees)
                {
                    m_log.InfoFormat("[TREES]: Activating Trees");
                    m_active_trees = true;
                    activeizeTreeze(m_active_trees);
                }
                else if (cmdparams[2] == "false" && m_active_trees)
                {
                    m_log.InfoFormat("[TREES]: Trees no longer active, for now...");
                    m_active_trees = false;
                    activeizeTreeze(m_active_trees);
                }
                else
                {
                    m_log.InfoFormat("[TREES]: When setting the tree module active via the console, you must specify true or false");
                }
            }
            else if (cmdparams[1] == "plant")
            {
                m_log.InfoFormat("[TREES]: New tree planting");
                UUID uuid = m_scene.RegionInfo.EstateSettings.EstateOwner;
                if (uuid == UUID.Zero)
                    uuid = m_scene.RegionInfo.MasterAvatarAssignedUUID;
                CreateTree(uuid, new Vector3(128.0f, 128.0f, 0.0f));
            }
            else
            {
                m_log.InfoFormat("[TREES]: Unknown command");
            }
        }

        private void activeizeTreeze(bool activeYN)
        {
            if (activeYN)
            {
                CalculateTrees = new Timer(m_tree_updates);
                CalculateTrees.Elapsed += CalculateTrees_Elapsed;
                CalculateTrees.Start();
            }
            else 
            {
                 CalculateTrees.Stop();
            }
        } 

        private void growTrees()
        {
            foreach (UUID tree in m_trees)
            {
                if (m_scene.Entities.ContainsKey(tree))
                {
                    SceneObjectPart s_tree = ((SceneObjectGroup) m_scene.Entities[tree]).RootPart;

                    // 100 seconds to grow 1m
                    s_tree.Scale += new Vector3(0.1f, 0.1f, 0.1f);
                    s_tree.SendFullUpdateToAllClients();
                    //s_tree.ScheduleTerseUpdate();
                }
                else
                {
                    m_trees.Remove(tree);
                }
            }
        }

        private void seedTrees()
        {
            foreach (UUID tree in m_trees)
            {
                if (m_scene.Entities.ContainsKey(tree))
                {
                    SceneObjectPart s_tree = ((SceneObjectGroup) m_scene.Entities[tree]).RootPart;

                    if (s_tree.Scale.X > 0.5)
                    {
                        if (Util.RandomClass.NextDouble() > 0.75)
                        {
                            SpawnChild(s_tree);
                        }
                    }
                }
                else
                {
                    m_trees.Remove(tree);
                }
            }
        }

        private void killTrees()
        {
            foreach (UUID tree in m_trees)
            {
                double killLikelyhood = 0.0;

                if (m_scene.Entities.ContainsKey(tree))
                {
                    SceneObjectPart selectedTree = ((SceneObjectGroup) m_scene.Entities[tree]).RootPart;
                    double selectedTreeScale = Math.Sqrt(Math.Pow(selectedTree.Scale.X, 2) +
                                                         Math.Pow(selectedTree.Scale.Y, 2) +
                                                         Math.Pow(selectedTree.Scale.Z, 2));

                    foreach (UUID picktree in m_trees)
                    {
                        if (picktree != tree)
                        {
                            SceneObjectPart pickedTree = ((SceneObjectGroup) m_scene.Entities[picktree]).RootPart;

                            double pickedTreeScale = Math.Sqrt(Math.Pow(pickedTree.Scale.X, 2) +
                                                               Math.Pow(pickedTree.Scale.Y, 2) +
                                                               Math.Pow(pickedTree.Scale.Z, 2));

                            double pickedTreeDistance = Math.Sqrt(Math.Pow(Math.Abs(pickedTree.AbsolutePosition.X - selectedTree.AbsolutePosition.X), 2) +
                                                                  Math.Pow(Math.Abs(pickedTree.AbsolutePosition.Y - selectedTree.AbsolutePosition.Y), 2) +
                                                                  Math.Pow(Math.Abs(pickedTree.AbsolutePosition.Z - selectedTree.AbsolutePosition.Z), 2));

                            killLikelyhood += (selectedTreeScale / (pickedTreeScale * pickedTreeDistance)) * 0.1;
                        }
                    }

                    if (Util.RandomClass.NextDouble() < killLikelyhood)
                    {
                        m_scene.DeleteSceneObject(selectedTree.ParentGroup, false);
                        m_trees.Remove(selectedTree.ParentGroup.UUID);

                        m_scene.ForEachClient(delegate(IClientAPI controller)
                                                  {
                                                      controller.SendKillObject(m_scene.RegionInfo.RegionHandle,
                                                                                selectedTree.LocalId);
                                                  });

                        break;
                    }
                    selectedTree.SetText(killLikelyhood.ToString(), new Vector3(1.0f, 1.0f, 1.0f), 1.0);
                }
                else
                {
                    m_trees.Remove(tree);
                }
            }
        }

        private void SpawnChild(SceneObjectPart s_tree)
        {
            Vector3 position = new Vector3();

            position.X = s_tree.AbsolutePosition.X + (1 * (-1 * Util.RandomClass.Next(1)));
            if (position.X > 255)
                position.X = 255;
            if (position.X < 0)
                position.X = 0;
            position.Y = s_tree.AbsolutePosition.Y + (1 * (-1 * Util.RandomClass.Next(1)));
            if (position.Y > 255)
                position.Y = 255;
            if (position.Y < 0)
                position.Y = 0;

            double randX = ((Util.RandomClass.NextDouble() * 2.0) - 1.0) * (s_tree.Scale.X * 3);
            double randY = ((Util.RandomClass.NextDouble() * 2.0) - 1.0) * (s_tree.Scale.X * 3);

            position.X += (float) randX;
            position.Y += (float) randY;

            UUID uuid = m_scene.RegionInfo.EstateSettings.EstateOwner;
            if (uuid == UUID.Zero)
                uuid = m_scene.RegionInfo.MasterAvatarAssignedUUID;

            CreateTree(uuid, position);
        }

        private void CreateTree(UUID uuid, Vector3 position)
        {
            position.Z = (float) m_scene.Heightmap[(int) position.X, (int) position.Y];

            IVegetationModule module = m_scene.RequestModuleInterface<IVegetationModule>();
            
            if (null == module)
                return;
            
            SceneObjectGroup tree 
                = module.AddTree(
                    uuid, UUID.Zero, new Vector3(0.1f, 0.1f, 0.1f), Quaternion.Identity, position, Tree.Cypress1, false);
            
            m_trees.Add(tree.UUID);
            tree.SendGroupFullUpdate();
        }

        private void CalculateTrees_Elapsed(object sender, ElapsedEventArgs e)
        {
            growTrees();
            seedTrees();
            killTrees();
        }
    }
}
