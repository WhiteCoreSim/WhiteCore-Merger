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
 *     * Neither the name of the OpenSimulator Project nor the
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
using System.Net;
using System.Reflection;
using System.Threading;

using OpenSim.Framework;
using OpenSim.Framework.Capabilities;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

using GridRegion = OpenSim.Services.Interfaces.GridRegion;

using OpenMetaverse;
using log4net;
using Nini.Config;

namespace Aurora.Modules
{
    public class EntityTransferModule : ISharedRegionModule, IEntityTransferModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected bool m_Enabled = false;
        protected Scene m_aScene;
        protected List<UUID> m_agentsInTransit;
        protected List<UUID> m_cancelingAgents;
        protected IWComms m_IWC;

        #region ISharedRegionModule

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public virtual string Name
        {
            get { return "IWCEntityTransferModule"; }
        }

        public virtual void Initialise(IConfigSource source)
        {
            IConfig moduleConfig = source.Configs["Modules"];
            if (moduleConfig != null)
            {
                string name = moduleConfig.GetString("EntityTransferModule", "");
                if (name == Name)
                {
                    m_agentsInTransit = new List<UUID>();
                    m_cancelingAgents = new List<UUID>();
                    m_Enabled = true;
                    //m_log.InfoFormat("[ENTITY TRANSFER MODULE]: {0} enabled.", Name);
                }
            }
        }

        public virtual void PostInitialise()
        {
        }

        public virtual void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            if (m_aScene == null)
                m_aScene = scene;

            scene.RegisterModuleInterface<IEntityTransferModule>(this);
            scene.EventManager.OnNewClient += OnNewClient;
            scene.EventManager.OnClosingClient += OnClosingClient;
        }

        protected virtual void OnNewClient(IClientAPI client)
        {
            client.OnTeleportHomeRequest += TeleportHome;
        }

        protected virtual void OnClosingClient(IClientAPI client)
        {
            client.OnTeleportHomeRequest -= TeleportHome;
        }

        public virtual void Close()
        {
            if (!m_Enabled)
                return;
        }


        public virtual void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;
            if (scene == m_aScene)
                m_aScene = null;

            scene.UnregisterModuleInterface<IEntityTransferModule>(this);
            scene.EventManager.OnNewClient -= OnNewClient;
            scene.EventManager.OnClosingClient -= OnClosingClient;
        }

        public virtual void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;
            m_IWC = scene.RequestModuleInterface<IWComms>();
        }


        #endregion

        #region Agent Teleports

        public virtual void Teleport(ScenePresence sp, ulong regionHandle, Vector3 position, Vector3 lookAt, uint teleportFlags)
        {
            string reason = "";
            if (!sp.Scene.Permissions.CanTeleport(sp.UUID, position, sp.Scene.AuthenticateHandler.GetAgentCircuitData(sp.UUID), out position, out reason))
            {
                sp.ControllingClient.SendTeleportFailed(reason);
                return;
            }

            sp.ControllingClient.SendTeleportStart(teleportFlags);
            sp.ControllingClient.SendTeleportProgress(teleportFlags, "requesting");
            //sp.ControllingClient.SendTeleportProgress("resolving");

            IEventQueue eq = sp.Scene.RequestModuleInterface<IEventQueue>();

            // Reset animations; the viewer does that in teleports.
            if (sp.Animator != null)
                sp.Animator.ResetAnimations();

            try
            {
                if (regionHandle == sp.Scene.RegionInfo.RegionHandle)
                {
                    // m_log.DebugFormat(
                    //    "[ENTITY TRANSFER MODULE]: RequestTeleportToLocation {0} within {1}",
                    //    position, sp.Scene.RegionInfo.RegionName);

                    // Teleport within the same region
                    if (IsOutsideRegion(sp.Scene, position) || position.Z < 0)
                    {
                        Vector3 emergencyPos = new Vector3(128, 128, 128);

                        m_log.WarnFormat(
                            "[ENTITY TRANSFER MODULE]: RequestTeleportToLocation() was given an illegal position of {0} for avatar {1}, {2}.  Substituting {3}",
                            position, sp.Name, sp.UUID, emergencyPos);
                        position = emergencyPos;
                    }

                    // TODO: Get proper AVG Height
                    float localAVHeight = 1.56f;
                    float posZLimit = 22;

                    // TODO: Check other Scene HeightField
                    if (position.X > 0 && position.X <= (int)Constants.RegionSize && position.Y > 0 && position.Y <= (int)Constants.RegionSize)
                    {
                        posZLimit = (float)sp.Scene.Heightmap[(int)position.X, (int)position.Y];
                    }

                    float newPosZ = posZLimit + localAVHeight;
                    if (posZLimit >= (position.Z - (localAVHeight / 2)) && !(Single.IsInfinity(newPosZ) || Single.IsNaN(newPosZ)))
                    {
                        position.Z = newPosZ;
                    }

                    sp.ControllingClient.SendLocalTeleport(position, lookAt, teleportFlags);
                    sp.Teleport(position);

                    foreach (SceneObjectGroup grp in sp.Attachments)
                        foreach (SceneObjectPart part in grp.ChildrenList)
                            sp.Scene.EventManager.TriggerOnScriptChangedEvent(part, (uint)Changed.TELEPORT);
                }
                else // Another region possibly in another simulator
                {
                    uint x = 0, y = 0;
                    Utils.LongToUInts(regionHandle, out x, out y);
                    GridRegion reg = m_aScene.GridService.GetRegionByPosition(sp.Scene.RegionInfo.ScopeID, (int)x, (int)y);

                    if (reg != null)
                    {
                        GridRegion finalDestination = GetFinalDestination(reg);
                        if (finalDestination == null)
                        {
                            m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Final destination is having problems. Unable to teleport agent.");
                            sp.ControllingClient.SendTeleportFailed("Problem at destination");
                            return;
                        }
                        //m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Final destination is x={0} y={1} uuid={2}",
                        //    finalDestination.RegionLocX / Constants.RegionSize, finalDestination.RegionLocY / Constants.RegionSize, finalDestination.RegionID);

                        if(m_IWC != null)
                            m_IWC.TeleportingAgent(sp, finalDestination);

                        //
                        // This is it
                        //
                        DoTeleport(sp, reg, finalDestination, position, lookAt, teleportFlags, eq);
                        //
                        //
                        //
                    }
                    else
                    {
                        // TP to a place that doesn't exist (anymore)
                        // Inform the viewer about that
                        sp.ControllingClient.SendTeleportFailed("The region you tried to teleport to doesn't exist anymore");

                        // and set the map-tile to '(Offline)'
                        uint regX, regY;
                        Utils.LongToUInts(regionHandle, out regX, out regY);

                        MapBlockData block = new MapBlockData();
                        block.X = (ushort)(regX / Constants.RegionSize);
                        block.Y = (ushort)(regY / Constants.RegionSize);
                        block.Access = 254; // == not there

                        List<MapBlockData> blocks = new List<MapBlockData>();
                        blocks.Add(block);
                        sp.ControllingClient.SendMapBlock(blocks, 0);
                    }
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Exception on teleport: {0}\n{1}", e.Message, e.StackTrace);
                sp.ControllingClient.SendTeleportFailed("Internal error");
            }
        }

        public virtual void DoTeleport(ScenePresence sp, GridRegion reg, GridRegion finalDestination, Vector3 position, Vector3 lookAt, uint teleportFlags, IEventQueue eq)
        {
            sp.ControllingClient.SendTeleportProgress(teleportFlags, "sending_dest");
            if (reg == null || finalDestination == null)
            {
                sp.ControllingClient.SendTeleportFailed("Unable to locate destination");
                return;
            }

            m_log.DebugFormat(
                "[ENTITY TRANSFER MODULE]: Request Teleport to {0}:{1}:{2}/{3}",
                reg.ExternalHostName, reg.HttpPort, finalDestination.RegionName, position);

            uint newRegionX = (uint)(reg.RegionHandle >> 40);
            uint newRegionY = (((uint)(reg.RegionHandle)) >> 8);
            uint oldRegionX = (uint)(sp.Scene.RegionInfo.RegionHandle >> 40);
            uint oldRegionY = (((uint)(sp.Scene.RegionInfo.RegionHandle)) >> 8);

            ulong destinationHandle = finalDestination.RegionHandle;

            // Let's do DNS resolution only once in this process, please!
            // This may be a costly operation. The reg.ExternalEndPoint field is not a passive field,
            // it's actually doing a lot of work.
            IPEndPoint endPoint = finalDestination.ExternalEndPoint;
            if (endPoint.Address != null)
            {
                sp.ControllingClient.SendTeleportProgress(teleportFlags, "arriving");

                if (m_cancelingAgents.Contains(sp.UUID))
                {
                    Cancel(sp);
                    return;
                }
                // Fixing a bug where teleporting while sitting results in the avatar ending up removed from
                // both regions
                if (sp.ParentID != UUID.Zero)
                    sp.StandUp(true);

                if (!sp.ValidateAttachments())
                {
                    sp.ControllingClient.SendTeleportProgress(teleportFlags, "missing_attach_tport");
                    sp.ControllingClient.SendTeleportFailed("Inconsistent attachment state");
                    return;
                }

                string capsPath = String.Empty;

                AgentCircuitData currentAgentCircuit = sp.Scene.AuthenticateHandler.GetAgentCircuitData(sp.ControllingClient.CircuitCode);
                AgentCircuitData agentCircuit = sp.ControllingClient.RequestClientInfo();
                agentCircuit.startpos = position;
                agentCircuit.child = true;
                agentCircuit.Appearance = sp.Appearance;
                if (currentAgentCircuit != null)
                {
                    agentCircuit.ServiceURLs = currentAgentCircuit.ServiceURLs;
                    agentCircuit.Viewer = currentAgentCircuit.Viewer;
                    agentCircuit.IPAddress = currentAgentCircuit.IPAddress;
                }

                if (NeedsNewAgent(oldRegionX, newRegionX, oldRegionY, newRegionY))
                {
                    // brand new agent, let's create a new caps seed
                    agentCircuit.CapsPath = CapsUtil.GetRandomCapsObjectPath();
                }

                string reason = String.Empty;
                // Let's create an agent there if one doesn't exist yet. 
                if (!CreateAgent(sp, reg, finalDestination, agentCircuit, teleportFlags, out reason))
                {
                    sp.ControllingClient.SendTeleportFailed(String.Format("Destination refused: {0}",
                                                                              reason));
                    return;
                }

                if (NeedsNewAgent(oldRegionX, newRegionX, oldRegionY, newRegionY))
                {
                    #region IP Translation for NAT
                    IClientIPEndpoint ipepClient;
                    if (sp.ClientView.TryGet(out ipepClient))
                    {
                        capsPath
                            = "http://"
                              + NetworkUtil.GetHostFor(ipepClient.EndPoint, finalDestination.ExternalHostName)
                              + ":"
                              + finalDestination.HttpPort
                              + CapsUtil.GetCapsSeedPath(agentCircuit.CapsPath);
                    }
                    else
                    {
                        capsPath
                            = "http://"
                              + finalDestination.ExternalHostName
                              + ":"
                              + finalDestination.HttpPort
                              + CapsUtil.GetCapsSeedPath(agentCircuit.CapsPath);
                    }
                    #endregion

                    if (eq != null)
                    {
                        #region IP Translation for NAT
                        // Uses ipepClient above
                        if (sp.ClientView.TryGet(out ipepClient))
                        {
                            endPoint.Address = NetworkUtil.GetIPFor(ipepClient.EndPoint, endPoint.Address);
                        }
                        #endregion

                        eq.EnableSimulator(destinationHandle, endPoint, sp.UUID);

                        // ES makes the client send a UseCircuitCode message to the destination, 
                        // which triggers a bunch of things there.
                        // So let's wait
                        Thread.Sleep(200);

                        eq.EstablishAgentCommunication(sp.UUID, endPoint, capsPath);

                    }
                    else
                    {
                        sp.ControllingClient.InformClientOfNeighbour(destinationHandle, endPoint);
                    }
                }
                else
                {
                    ICapabilitiesModule module = sp.Scene.RequestModuleInterface<ICapabilitiesModule>();
                    if (module != null)
                        agentCircuit.CapsPath = module.GetChildSeed(sp.UUID, reg.RegionHandle);
                    capsPath = "http://" + finalDestination.ExternalHostName + ":" + finalDestination.HttpPort
                                + "/CAPS/" + agentCircuit.CapsPath + "0000/";
                }

                if (m_cancelingAgents.Contains(sp.UUID))
                {
                    Cancel(sp);
                    return;
                }

                m_log.DebugFormat(
                    "[ENTITY TRANSFER MODULE]: Sending new CAPS seed url {0} to client {1}", capsPath, sp.UUID);

                if (eq != null)
                {
                    eq.TeleportFinishEvent(destinationHandle, finalDestination.Access, endPoint,
                                           0, teleportFlags, capsPath, sp.UUID, teleportFlags);
                }
                else
                {
                    sp.ControllingClient.SendRegionTeleport(destinationHandle, 13, endPoint, 4,
                                                                teleportFlags, capsPath);
                }

                SetInTransit(sp.UUID);

                // Let's send a full update of the agent. This is a synchronous call.
                AgentData agent = new AgentData();
                sp.CopyTo(agent);
                agent.Position = position;
                SetCallbackURL(agent, sp.Scene.RegionInfo);

                if (!UpdateAgent(reg, finalDestination, agent))
                {
                    // Region doesn't take it
                    Fail(sp, finalDestination);
                    return;
                }

                // Let's set this to true tentatively. This does not trigger OnChildAgent
                sp.IsChildAgent = true;

                // TeleportFinish makes the client send CompleteMovementIntoRegion (at the destination), which
                // trigers a whole shebang of things there, including MakeRoot. So let's wait for confirmation
                // that the client contacted the destination before we send the attachments and close things here.
                if (!WaitForCallback(sp.UUID))
                {
                    //Fail(sp, finalDestination);
                    //return;
                }


                // CrossAttachmentsIntoNewRegion is a synchronous call. We shouldn't need to wait after it
                CrossAttachmentsIntoNewRegion(finalDestination, sp, true);

                KillEntity(sp.Scene, sp.LocalId);

                // Now let's make it officially a child agent
                sp.MakeChildAgent();

                sp.Scene.CleanDroppedAttachments();

                // Finally, let's close this previously-known-as-root agent, when the jump is outside the view zone

                // OK, it got this agent. Let's close some child agents
                sp.CloseChildAgents(newRegionX, newRegionY);

                if (NeedsClosing(oldRegionX, newRegionX, oldRegionY, newRegionY, reg))
                {
                    Thread.Sleep(5000);
                    sp.Close();
                    sp.Scene.IncomingCloseAgent(sp.UUID);
                }
                else
                    // now we have a child agent in this region. 
                    sp.Reset();

                //If they canceled too late, remove them so the next tp does not fail.
                if (m_cancelingAgents.Contains(sp.UUID))
                    m_cancelingAgents.Remove(sp.UUID);
            }
            else
            {
                sp.ControllingClient.SendTeleportFailed("Remote Region appears to be down");
            }
        }

        private void Cancel(ScenePresence sp)
        {
            m_cancelingAgents.Remove(sp.UUID);

            // Fail. Reset it back
            sp.IsChildAgent = false;

            ResetFromTransit(sp.UUID);

            EnableChildAgents(sp);
        }

        private void Fail(ScenePresence sp, GridRegion finalDestination)
        {
            // Client never contacted destination. Let's restore everything back
            sp.ControllingClient.SendTeleportFailed("Problems connecting to destination.");

            // Fail. Reset it back
            sp.IsChildAgent = false;

            ResetFromTransit(sp.UUID);

            EnableChildAgents(sp);

            // Finally, kill the agent we just created at the destination.
            m_aScene.SimulationService.CloseAgent(finalDestination, sp.UUID);

        }

        protected virtual bool CreateAgent(ScenePresence sp, GridRegion reg, GridRegion finalDestination, AgentCircuitData agentCircuit, uint teleportFlags, out string reason)
        {
            return m_aScene.SimulationService.CreateAgent(finalDestination, agentCircuit, teleportFlags, out reason);
        }

        protected virtual bool UpdateAgent(GridRegion reg, GridRegion finalDestination, AgentData agent)
        {
            return m_aScene.SimulationService.UpdateAgent(finalDestination, agent);
        }

        protected virtual void SetCallbackURL(AgentData agent, RegionInfo region)
        {
            agent.CallbackURI = "http://" + region.ExternalHostName + ":" + region.HttpPort +
                "/agent/" + agent.AgentID.ToString() + "/" + region.RegionID.ToString() + "/release/";

        }

        protected void KillEntity(Scene scene, uint localID)
        {
            scene.SendKillObject(localID);
        }

        protected virtual GridRegion GetFinalDestination(GridRegion region)
        {
            return region;
        }

        protected virtual bool NeedsNewAgent(uint oldRegionX, uint newRegionX, uint oldRegionY, uint newRegionY)
        {
            return Util.IsOutsideView(oldRegionX, newRegionX, oldRegionY, newRegionY, false);
        }

        protected virtual bool NeedsClosing(uint oldRegionX, uint newRegionX, uint oldRegionY, uint newRegionY, GridRegion reg)
        {
            return Util.IsOutsideView(oldRegionX, newRegionX, oldRegionY, newRegionY, Util.GetIsLocalRegion(reg.RegionHandle));
        }

        protected virtual bool IsOutsideRegion(Scene s, Vector3 pos)
        {

            if (s.TestBorderCross(pos, Cardinals.N))
                return true;
            if (s.TestBorderCross(pos, Cardinals.S))
                return true;
            if (s.TestBorderCross(pos, Cardinals.E))
                return true;
            if (s.TestBorderCross(pos, Cardinals.W))
                return true;

            return false;
        }


        #endregion

        #region Teleport Home

        public virtual void TeleportHome(UUID id, IClientAPI client)
        {
            //m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Request to teleport {0} {1} home", client.FirstName, client.LastName);

            //OpenSim.Services.Interfaces.PresenceInfo pinfo = m_aScene.PresenceService.GetAgent(client.SessionId);
            GridUserInfo uinfo = m_aScene.GridUserService.GetGridUserInfo(client.AgentId.ToString());

            if (uinfo != null)
            {
                GridRegion regionInfo = m_aScene.GridService.GetRegionByUUID(UUID.Zero, uinfo.HomeRegionID);
                if (regionInfo == null)
                {
                    // can't find the Home region: Tell viewer and abort
                    client.SendTeleportFailed("Your home region could not be found.");
                    return;
                }
                m_log.DebugFormat("[ENTITY TRANSFER MODULE]: User's home region is {0} {1} ({2}-{3})",
                    regionInfo.RegionName, regionInfo.RegionID, regionInfo.RegionLocX / Constants.RegionSize, regionInfo.RegionLocY / Constants.RegionSize);

                // a little eekie that this goes back to Scene and with a forced cast, will fix that at some point...
                ((Scene)(client.Scene)).RequestTeleportLocation(
                    client, regionInfo.RegionHandle, uinfo.HomePosition, uinfo.HomeLookAt,
                    (uint)(Constants.TeleportFlags.SetLastToTarget | Constants.TeleportFlags.ViaHome));
            }
            else
            {
                //Default region time...
                List<GridRegion> Regions = m_aScene.GridService.GetDefaultRegions(UUID.Zero);
                if (Regions.Count != 0)
                {

                    m_log.DebugFormat("[ENTITY TRANSFER MODULE]: User's home region was not found, using {0} {1} ({2}-{3})",
                        Regions[0].RegionName, Regions[0].RegionID, Regions[0].RegionLocX / Constants.RegionSize, Regions[0].RegionLocY / Constants.RegionSize);

                    // a little eekie that this goes back to Scene and with a forced cast, will fix that at some point...
                    ((Scene)(client.Scene)).RequestTeleportLocation(
                        client, Regions[0].RegionHandle, new Vector3(128, 128, 25), new Vector3(128, 128, 128),
                        (uint)(Constants.TeleportFlags.SetLastToTarget | Constants.TeleportFlags.ViaHome));
                }
            }
        }

        #endregion


        #region Agent Crossings

        public virtual void Cross(ScenePresence agent, bool isFlying)
        {
            Scene scene = agent.Scene;
            Vector3 pos = agent.AbsolutePosition;
            Vector3 newpos = new Vector3(pos.X, pos.Y, pos.Z);
            uint neighbourx = scene.RegionInfo.RegionLocX;
            uint neighboury = scene.RegionInfo.RegionLocY;
            const float boundaryDistance = 1.7f;
            Vector3 northCross = new Vector3(0, boundaryDistance, 0);
            Vector3 southCross = new Vector3(0, -1 * boundaryDistance, 0);
            Vector3 eastCross = new Vector3(boundaryDistance, 0, 0);
            Vector3 westCross = new Vector3(-1 * boundaryDistance, 0, 0);

            // distance to edge that will trigger crossing


            // distance into new region to place avatar
            const float enterDistance = 0.5f;

            if (scene.TestBorderCross(pos + westCross, Cardinals.W))
            {
                if (scene.TestBorderCross(pos + northCross, Cardinals.N))
                {
                    Border b = scene.GetCrossedBorder(pos + northCross, Cardinals.N);
                    neighboury += (uint)(int)(b.BorderLine.Z / (int)Constants.RegionSize);
                }
                else if (scene.TestBorderCross(pos + southCross, Cardinals.S))
                {
                    Border b = scene.GetCrossedBorder(pos + southCross, Cardinals.S);
                    if (b.TriggerRegionX == 0 && b.TriggerRegionY == 0)
                    {
                        neighboury--;
                        newpos.Y = Constants.RegionSize - enterDistance;
                    }
                    else
                    {
                        neighboury = b.TriggerRegionY;
                        neighbourx = b.TriggerRegionX;

                        Vector3 newposition = pos;
                        newposition.X += (scene.RegionInfo.RegionLocX - neighbourx) * Constants.RegionSize;
                        newposition.Y += (scene.RegionInfo.RegionLocY - neighboury) * Constants.RegionSize;
                        agent.ControllingClient.SendAgentAlertMessage(
                            String.Format("Moving you to region {0},{1}", neighbourx, neighboury), false);
                        InformClientToInitateTeleportToLocation(agent, neighbourx, neighboury, newposition, scene);
                        return;
                    }
                }

                Border ba = scene.GetCrossedBorder(pos + westCross, Cardinals.W);
                if (ba.TriggerRegionX == 0 && ba.TriggerRegionY == 0)
                {
                    neighbourx--;
                    newpos.X = Constants.RegionSize - enterDistance;
                }
                else
                {
                    neighboury = ba.TriggerRegionY;
                    neighbourx = ba.TriggerRegionX;


                    Vector3 newposition = pos;
                    newposition.X += (scene.RegionInfo.RegionLocX - neighbourx) * Constants.RegionSize;
                    newposition.Y += (scene.RegionInfo.RegionLocY - neighboury) * Constants.RegionSize;
                    agent.ControllingClient.SendAgentAlertMessage(
                            String.Format("Moving you to region {0},{1}", neighbourx, neighboury), false);
                    InformClientToInitateTeleportToLocation(agent, neighbourx, neighboury, newposition, scene);


                    return;
                }

            }
            else if (scene.TestBorderCross(pos + eastCross, Cardinals.E))
            {
                Border b = scene.GetCrossedBorder(pos + eastCross, Cardinals.E);
                neighbourx += (uint)(int)(b.BorderLine.Z / (int)Constants.RegionSize);
                newpos.X = enterDistance;

                if (scene.TestBorderCross(pos + southCross, Cardinals.S))
                {
                    Border ba = scene.GetCrossedBorder(pos + southCross, Cardinals.S);
                    if (ba.TriggerRegionX == 0 && ba.TriggerRegionY == 0)
                    {
                        neighboury--;
                        newpos.Y = Constants.RegionSize - enterDistance;
                    }
                    else
                    {
                        neighboury = ba.TriggerRegionY;
                        neighbourx = ba.TriggerRegionX;
                        Vector3 newposition = pos;
                        newposition.X += (scene.RegionInfo.RegionLocX - neighbourx) * Constants.RegionSize;
                        newposition.Y += (scene.RegionInfo.RegionLocY - neighboury) * Constants.RegionSize;
                        agent.ControllingClient.SendAgentAlertMessage(
                            String.Format("Moving you to region {0},{1}", neighbourx, neighboury), false);
                        InformClientToInitateTeleportToLocation(agent, neighbourx, neighboury, newposition, scene);
                        return;
                    }
                }
                else if (scene.TestBorderCross(pos + northCross, Cardinals.N))
                {
                    Border c = scene.GetCrossedBorder(pos + northCross, Cardinals.N);
                    neighboury += (uint)(int)(c.BorderLine.Z / (int)Constants.RegionSize);
                    newpos.Y = enterDistance;
                }


            }
            else if (scene.TestBorderCross(pos + southCross, Cardinals.S))
            {
                Border b = scene.GetCrossedBorder(pos + southCross, Cardinals.S);
                if (b.TriggerRegionX == 0 && b.TriggerRegionY == 0)
                {
                    neighboury--;
                    newpos.Y = Constants.RegionSize - enterDistance;
                }
                else
                {
                    neighboury = b.TriggerRegionY;
                    neighbourx = b.TriggerRegionX;
                    Vector3 newposition = pos;
                    newposition.X += (scene.RegionInfo.RegionLocX - neighbourx) * Constants.RegionSize;
                    newposition.Y += (scene.RegionInfo.RegionLocY - neighboury) * Constants.RegionSize;
                    agent.ControllingClient.SendAgentAlertMessage(
                            String.Format("Moving you to region {0},{1}", neighbourx, neighboury), false);
                    InformClientToInitateTeleportToLocation(agent, neighbourx, neighboury, newposition, scene);
                    return;
                }
            }
            else if (scene.TestBorderCross(pos + northCross, Cardinals.N))
            {

                Border b = scene.GetCrossedBorder(pos + northCross, Cardinals.N);
                neighboury += (uint)(int)(b.BorderLine.Z / (int)Constants.RegionSize);
                newpos.Y = enterDistance;
            }

            /*

            if (pos.X < boundaryDistance) //West
            {
                neighbourx--;
                newpos.X = Constants.RegionSize - enterDistance;
            }
            else if (pos.X > Constants.RegionSize - boundaryDistance) // East
            {
                neighbourx++;
                newpos.X = enterDistance;
            }

            if (pos.Y < boundaryDistance) // South
            {
                neighboury--;
                newpos.Y = Constants.RegionSize - enterDistance;
            }
            else if (pos.Y > Constants.RegionSize - boundaryDistance) // North
            {
                neighboury++;
                newpos.Y = enterDistance;
            }
            */

            CrossAgentToNewRegionDelegate d = CrossAgentToNewRegionAsync;
            d.BeginInvoke(agent, newpos, neighbourx, neighboury, isFlying, CrossAgentToNewRegionCompleted, d);

        }


        public delegate void InformClientToInitateTeleportToLocationDelegate(ScenePresence agent, uint regionX, uint regionY,
                                                            Vector3 position,
                                                            Scene initiatingScene);

        protected void InformClientToInitateTeleportToLocation(ScenePresence agent, uint regionX, uint regionY, Vector3 position, Scene initiatingScene)
        {

            // This assumes that we know what our neighbors are.

            InformClientToInitateTeleportToLocationDelegate d = InformClientToInitiateTeleportToLocationAsync;
            d.BeginInvoke(agent, regionX, regionY, position, initiatingScene,
                          InformClientToInitiateTeleportToLocationCompleted,
                          d);
        }

        public void InformClientToInitiateTeleportToLocationAsync(ScenePresence agent, uint regionX, uint regionY, Vector3 position,
            Scene initiatingScene)
        {
            Thread.Sleep(10000);
            IMessageTransferModule im = initiatingScene.RequestModuleInterface<IMessageTransferModule>();
            if (im != null)
            {
                UUID gotoLocation = Util.BuildFakeParcelID(
                    Util.UIntsToLong(
                                              (regionX *
                                               (uint)Constants.RegionSize),
                                              (regionY *
                                               (uint)Constants.RegionSize)),
                    (uint)(int)position.X,
                    (uint)(int)position.Y,
                    (uint)(int)position.Z);
                GridInstantMessage m = new GridInstantMessage(initiatingScene, UUID.Zero,
                "Region", agent.UUID,
                (byte)InstantMessageDialog.GodLikeRequestTeleport, false,
                "", gotoLocation, false, new Vector3(127, 0, 0),
                new Byte[0]);
                im.SendInstantMessage(m, delegate(bool success)
                {
                    //m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Client Initiating Teleport sending IM success = {0}", success);
                });

            }
        }

        protected void InformClientToInitiateTeleportToLocationCompleted(IAsyncResult iar)
        {
            InformClientToInitateTeleportToLocationDelegate icon =
                (InformClientToInitateTeleportToLocationDelegate)iar.AsyncState;
            icon.EndInvoke(iar);
        }

        public delegate ScenePresence CrossAgentToNewRegionDelegate(ScenePresence agent, Vector3 pos, uint neighbourx, uint neighboury, bool isFlying);

        /// <summary>
        /// This Closes child agents on neighboring regions
        /// Calls an asynchronous method to do so..  so it doesn't lag the sim.
        /// </summary>
        protected ScenePresence CrossAgentToNewRegionAsync(ScenePresence agent, Vector3 pos, uint neighbourx, uint neighboury, bool isFlying)
        {
            m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Crossing agent {0} {1} to {2}-{3}", agent.Firstname, agent.Lastname, neighbourx, neighboury);

            Scene m_scene = agent.Scene;
            ulong neighbourHandle = Utils.UIntsToLong((uint)(neighbourx * Constants.RegionSize), (uint)(neighboury * Constants.RegionSize));

            int x = (int)(neighbourx * Constants.RegionSize), y = (int)(neighboury * Constants.RegionSize);
            GridRegion neighbourRegion = m_scene.GridService.GetRegionByPosition(m_scene.RegionInfo.ScopeID, (int)x, (int)y);

            if (neighbourRegion != null && agent.ValidateAttachments())
            {
                pos = pos + (agent.Velocity);

                SetInTransit(agent.UUID);
                AgentData cAgent = new AgentData();
                agent.CopyTo(cAgent);
                cAgent.Position = pos;
                if (isFlying)
                    cAgent.ControlFlags |= (uint)AgentManager.ControlFlags.AGENT_CONTROL_FLY;
                cAgent.CallbackURI = "http://" + m_scene.RegionInfo.ExternalHostName + ":" + m_scene.RegionInfo.HttpPort +
                    "/agent/" + agent.UUID.ToString() + "/" + m_scene.RegionInfo.RegionID.ToString() + "/release/";

                if (!m_scene.SimulationService.UpdateAgent(neighbourRegion, cAgent))
                {
                    // region doesn't take it
                    ResetFromTransit(agent.UUID);
                    return agent;
                }

                string agentcaps;
                if (!agent.KnownRegions.TryGetValue(neighbourRegion.RegionHandle, out agentcaps))
                {
                    m_log.ErrorFormat("[ENTITY TRANSFER MODULE]: No ENTITY TRANSFER MODULE information for region handle {0}, exiting CrossToNewRegion.",
                                     neighbourRegion.RegionHandle);
                    return agent;
                }
                // TODO Should construct this behind a method
                string capsPath =
                    "http://" + neighbourRegion.ExternalHostName + ":" + neighbourRegion.HttpPort
                     + "/CAPS/" + agentcaps /*circuitdata.CapsPath*/ + "0000/";

                m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Sending new CAPS seed url {0} to client {1}", capsPath, agent.UUID);

                IEventQueue eq = agent.Scene.RequestModuleInterface<IEventQueue>();
                if (eq != null)
                {
                    eq.CrossRegion(neighbourHandle, pos, agent.Velocity, neighbourRegion.ExternalEndPoint,
                                   capsPath, agent.UUID, agent.ControllingClient.SessionId);
                }
                else
                {
                    agent.ControllingClient.CrossRegion(neighbourHandle, pos, agent.Velocity, neighbourRegion.ExternalEndPoint,
                                                capsPath);
                }

                if (!WaitForCallback(agent.UUID))
                {
                    m_log.Debug("[ENTITY TRANSFER MODULE]: Callback never came in crossing agent");
                    ResetFromTransit(agent.UUID);

                    // Yikes! We should just have a ref to scene here.
                    //agent.Scene.InformClientOfNeighbours(agent);
                    EnableChildAgents(agent);

                    return agent;
                }

                // Next, let's close the child agent connections that are too far away.
                agent.CloseChildAgents(neighbourx, neighboury);

                agent.MakeChildAgent();
                // now we have a child agent in this region. Request all interesting data about other (root) agents
                agent.SendOtherAgentsAvatarDataToMe();
                agent.SendOtherAgentsAppearanceToMe();

                CrossAttachmentsIntoNewRegion(neighbourRegion, agent, true);
            }

            //m_log.Debug("AFTER CROSS");
            //Scene.DumpChildrenSeeds(UUID);
            //DumpKnownRegions();
            return agent;
        }

        /// <summary>
        /// This Closes child agents on neighboring regions
        /// Calls an asynchronous method to do so..  so it doesn't lag the sim.
        /// </summary>
        protected ScenePresence CrossAgentSittingToNewRegionAsync(ScenePresence agent, GridRegion neighbourRegion, SceneObjectGroup grp)
        {
            Scene m_scene = agent.Scene;

            if (agent.ValidateAttachments())
            {
                AgentData cAgent = new AgentData();
                agent.CopyTo(cAgent);
                cAgent.Position = grp.AbsolutePosition;


                cAgent.CallbackURI = "http://" + m_scene.RegionInfo.ExternalHostName + ":" + m_scene.RegionInfo.HttpPort +
                    "/agent/" + agent.UUID.ToString() + "/" + m_scene.RegionInfo.RegionID.ToString() + "/release/";

                if (!m_scene.SimulationService.UpdateAgent(neighbourRegion, cAgent))
                {
                    // region doesn't take it
                    ResetFromTransit(agent.UUID);
                    return agent;
                }

                // Next, let's close the child agent connections that are too far away.
                agent.CloseChildAgents((uint)neighbourRegion.RegionLocX / 256, (uint)neighbourRegion.RegionLocY / 256);

                string agentcaps;
                if (!agent.KnownRegions.TryGetValue(neighbourRegion.RegionHandle, out agentcaps))
                {
                    m_log.ErrorFormat("[ENTITY TRANSFER MODULE]: No ENTITY TRANSFER MODULE information for region handle {0}, exiting CrossToNewRegion.",
                                     neighbourRegion.RegionHandle);
                    return agent;
                }
                // TODO Should construct this behind a method
                string capsPath =
                    "http://" + neighbourRegion.ExternalHostName + ":" + neighbourRegion.HttpPort
                     + "/CAPS/" + agentcaps /*circuitdata.CapsPath*/ + "0000/";

                m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Sending new CAPS seed url {0} to client {1}", capsPath, agent.UUID);

                IEventQueue eq = agent.Scene.RequestModuleInterface<IEventQueue>();
                if (eq != null)
                {
                    eq.CrossRegion(neighbourRegion.RegionHandle, agent.AbsolutePosition, agent.Velocity, neighbourRegion.ExternalEndPoint,
                                   capsPath, agent.UUID, agent.ControllingClient.SessionId);
                }
                else
                {
                    agent.ControllingClient.CrossRegion(neighbourRegion.RegionHandle, agent.AbsolutePosition, agent.Velocity, neighbourRegion.ExternalEndPoint,
                                                capsPath);
                }

                agent.MakeChildAgent();
                // now we have a child agent in this region. Request all interesting data about other (root) agents
                agent.SendOtherAgentsAvatarDataToMe();
                agent.SendOtherAgentsAppearanceToMe();

                CrossAttachmentsIntoNewRegion(neighbourRegion, agent, true);
            }
            return agent;
        }

        protected void CrossAgentToNewRegionCompleted(IAsyncResult iar)
        {
            CrossAgentToNewRegionDelegate icon = (CrossAgentToNewRegionDelegate)iar.AsyncState;
            ScenePresence agent = icon.EndInvoke(iar);

            // If the cross was successful, this agent is a child agent
            if (agent.IsChildAgent)
                agent.Reset();
            else // Not successful
                agent.RestoreInCurrentScene();

            // In any case
            agent.NotInTransit();

            //m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Crossing agent {0} {1} completed.", agent.Firstname, agent.Lastname);
        }

        #endregion

        #region Enable Child Agent
        /// <summary>
        /// This informs a single neighboring region about agent "avatar".
        /// Calls an asynchronous method to do so..  so it doesn't lag the sim.
        /// </summary>
        public void EnableChildAgent(ScenePresence sp, GridRegion region)
        {
            m_log.DebugFormat("[ENTITY TRANSFER]: Enabling child agent in new neighour {0}", region.RegionName);

            AgentCircuitData currentAgentCircuit = sp.Scene.AuthenticateHandler.GetAgentCircuitData(sp.ControllingClient.CircuitCode);
            AgentCircuitData agent = sp.ControllingClient.RequestClientInfo();
            agent.BaseFolder = UUID.Zero;
            agent.InventoryFolder = UUID.Zero;
            agent.startpos = new Vector3(128, 128, 70);
            agent.child = true;
            agent.Appearance = sp.Appearance;
            agent.CapsPath = CapsUtil.GetRandomCapsObjectPath();
            ICapabilitiesModule module = sp.Scene.RequestModuleInterface<ICapabilitiesModule>();
            if(module != null)
                agent.ChildrenCapSeeds = new Dictionary<ulong, string>(module.GetChildrenSeeds(sp.UUID));
            m_log.DebugFormat("[XXX] Seeds 1 {0}", agent.ChildrenCapSeeds.Count);

            if (!agent.ChildrenCapSeeds.ContainsKey(sp.Scene.RegionInfo.RegionHandle))
                agent.ChildrenCapSeeds.Add(sp.Scene.RegionInfo.RegionHandle, sp.ControllingClient.RequestClientInfo().CapsPath);
            m_log.DebugFormat("[XXX] Seeds 2 {0}", agent.ChildrenCapSeeds.Count);

            sp.AddNeighbourRegion(region.RegionHandle, agent.CapsPath);
            foreach (ulong h in agent.ChildrenCapSeeds.Keys)
                m_log.DebugFormat("[XXX] --> {0}", h);
            m_log.DebugFormat("[XXX] Adding {0}", region.RegionHandle);
            agent.ChildrenCapSeeds.Add(region.RegionHandle, agent.CapsPath);

            if (module != null)
                module.SetChildrenSeed(sp.UUID, agent.ChildrenCapSeeds);

            if (currentAgentCircuit != null)
            {
                agent.ServiceURLs = currentAgentCircuit.ServiceURLs;
                agent.Viewer = currentAgentCircuit.Viewer;
                agent.IPAddress = currentAgentCircuit.IPAddress;
            }

            InformClientOfNeighbourDelegate d = InformClientOfNeighbourAsync;
            d.BeginInvoke(sp, agent, region, region.ExternalEndPoint, true,
                          InformClientOfNeighbourCompleted,
                          d);
        }
        #endregion

        #region Enable Child Agents

        private delegate void InformClientOfNeighbourDelegate(
            ScenePresence avatar, AgentCircuitData a, GridRegion reg, IPEndPoint endPoint, bool newAgent);

        /// <summary>
        /// This informs all neighboring regions about agent "avatar".
        /// Calls an asynchronous method to do so..  so it doesn't lag the sim.
        /// </summary>
        public void EnableChildAgents(ScenePresence sp)
        {
            List<GridRegion> neighbours = new List<GridRegion>();
            RegionInfo m_regionInfo = sp.Scene.RegionInfo;

            if (m_regionInfo != null)
            {
                neighbours = RequestNeighbours(sp.Scene, m_regionInfo.RegionLocX, m_regionInfo.RegionLocY);
            }
            else
            {
                m_log.Debug("[ENTITY TRANSFER MODULE]: m_regionInfo was null in EnableChildAgents, is this a NPC?");
            }

            /// We need to find the difference between the new regions where there are no child agents
            /// and the regions where there are already child agents. We only send notification to the former.
            List<ulong> neighbourHandles = NeighbourHandles(neighbours); // on this region
            neighbourHandles.Add(sp.Scene.RegionInfo.RegionHandle);  // add this region too
            List<ulong> previousRegionNeighbourHandles;

            ICapabilitiesModule module = sp.Scene.RequestModuleInterface<ICapabilitiesModule>();
            if (module != null)
            {
                previousRegionNeighbourHandles =
                    new List<ulong>(module.GetChildrenSeeds(sp.UUID).Keys);
            }
            else
            {
                previousRegionNeighbourHandles = new List<ulong>();
            }

            List<ulong> newRegions = NewNeighbours(neighbourHandles, previousRegionNeighbourHandles);
            List<ulong> oldRegions = OldNeighbours(neighbourHandles, previousRegionNeighbourHandles);

            //Dump("Current Neighbors", neighbourHandles);
            //Dump("Previous Neighbours", previousRegionNeighbourHandles);
            //Dump("New Neighbours", newRegions);
            //Dump("Old Neighbours", oldRegions);

            /// Update the scene presence's known regions here on this region
            sp.DropOldNeighbours(oldRegions);

            /// Collect as many seeds as possible
            Dictionary<ulong, string> seeds;
            if (module != null)
                seeds
                    = new Dictionary<ulong, string>(module.GetChildrenSeeds(sp.UUID));
            else
                seeds = new Dictionary<ulong, string>();

            //m_log.Debug(" !!! No. of seeds: " + seeds.Count);
            if (!seeds.ContainsKey(sp.Scene.RegionInfo.RegionHandle))
                seeds.Add(sp.Scene.RegionInfo.RegionHandle, sp.ControllingClient.RequestClientInfo().CapsPath);

            /// Create the necessary child agents
            List<AgentCircuitData> cagents = new List<AgentCircuitData>();
            foreach (GridRegion neighbour in neighbours)
            {
                if (neighbour.RegionHandle != sp.Scene.RegionInfo.RegionHandle)
                {

                    AgentCircuitData currentAgentCircuit = sp.Scene.AuthenticateHandler.GetAgentCircuitData(sp.ControllingClient.CircuitCode);
                    AgentCircuitData agent = sp.ControllingClient.RequestClientInfo();
                    agent.BaseFolder = UUID.Zero;
                    agent.InventoryFolder = UUID.Zero;
                    agent.startpos = new Vector3(128, 128, 70);
                    agent.child = true;
                    agent.Appearance = sp.Appearance;
                    if (currentAgentCircuit != null)
                    {
                        agent.ServiceURLs = currentAgentCircuit.ServiceURLs;
                        agent.Viewer = currentAgentCircuit.Viewer;
                        agent.IPAddress = currentAgentCircuit.IPAddress;
                    }

                    if (newRegions.Contains(neighbour.RegionHandle))
                    {
                        agent.CapsPath = CapsUtil.GetRandomCapsObjectPath();
                        sp.AddNeighbourRegion(neighbour.RegionHandle, agent.CapsPath);
                        seeds.Add(neighbour.RegionHandle, agent.CapsPath);
                    }
                    else if(module != null)
                        agent.CapsPath = module.GetChildSeed(sp.UUID, neighbour.RegionHandle);

                    cagents.Add(agent);
                }
            }

            /// Update all child agent with everyone's seeds
            foreach (AgentCircuitData a in cagents)
            {
                a.ChildrenCapSeeds = new Dictionary<ulong, string>(seeds);
            }

            if (module != null)
            {
                module.SetChildrenSeed(sp.UUID, seeds);
            }
            sp.KnownRegions = seeds;
            //avatar.Scene.DumpChildrenSeeds(avatar.UUID);
            //avatar.DumpKnownRegions();

            bool newAgent = false;
            int count = 0;
            foreach (GridRegion neighbour in neighbours)
            {
                //m_log.WarnFormat("--> Going to send child agent to {0}", neighbour.RegionName);
                // Don't do it if there's already an agent in that region
                if (newRegions.Contains(neighbour.RegionHandle))
                    newAgent = true;
                else
                    newAgent = false;

                if (neighbour.RegionHandle != sp.Scene.RegionInfo.RegionHandle)
                {
                    InformClientOfNeighbourDelegate d = InformClientOfNeighbourAsync;
                    try
                    {
                        d.BeginInvoke(sp, cagents[count], neighbour, neighbour.ExternalEndPoint, newAgent,
                                      InformClientOfNeighbourCompleted,
                                      d);
                    }

                    catch (ArgumentOutOfRangeException)
                    {
                        m_log.ErrorFormat(
                           "[ENTITY TRANSFER MODULE]: Neighbour Regions response included the current region in the neighbor list.  The following region will not display to the client: {0} for region {1} ({2}, {3}).",
                           neighbour.ExternalHostName,
                           neighbour.RegionHandle,
                           neighbour.RegionLocX,
                           neighbour.RegionLocY);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[ENTITY TRANSFER MODULE]: Could not resolve external hostname {0} for region {1} ({2}, {3}).  {4}",
                            neighbour.ExternalHostName,
                            neighbour.RegionHandle,
                            neighbour.RegionLocX,
                            neighbour.RegionLocY,
                            e);

                        // FIXME: Okay, even though we've failed, we're still going to throw the exception on,
                        // since I don't know what will happen if we just let the client continue

                        // XXX: Well, decided to swallow the exception instead for now.  Let us see how that goes.
                        // throw e;

                    }
                }
                count++;
            }
        }

        private void InformClientOfNeighbourCompleted(IAsyncResult iar)
        {
            InformClientOfNeighbourDelegate icon = (InformClientOfNeighbourDelegate)iar.AsyncState;
            icon.EndInvoke(iar);
            //m_log.WarnFormat(" --> InformClientOfNeighbourCompleted");
        }

        /// <summary>
        /// Async component for informing client of which neighbours exist
        /// </summary>
        /// <remarks>
        /// This needs to run asynchronously, as a network timeout may block the thread for a long while
        /// </remarks>
        /// <param name="remoteClient"></param>
        /// <param name="a"></param>
        /// <param name="regionHandle"></param>
        /// <param name="endPoint"></param>
        private void InformClientOfNeighbourAsync(ScenePresence sp, AgentCircuitData a, GridRegion reg,
                                                  IPEndPoint endPoint, bool newAgent)
        {
            // Let's wait just a little to give time to originating regions to catch up with closing child agents
            // after a cross here
            //Thread.Sleep(500);

            Scene m_scene = sp.Scene;

            uint x, y;
            Utils.LongToUInts(reg.RegionHandle, out x, out y);
            x = x / Constants.RegionSize;
            y = y / Constants.RegionSize;
            //m_log.Info("[ENTITY TRANSFER MODULE]: Starting to inform client about neighbour " + x + ", " + y + "(" + endPoint.ToString() + ")");

            string capsPath = "http://" + reg.ExternalHostName + ":" + reg.HttpPort
                  + "/CAPS/" + a.CapsPath + "0000/";

            string reason = String.Empty;


            bool regionAccepted = m_scene.SimulationService.CreateAgent(reg, a, (uint)TeleportFlags.Default, out reason);

            if (regionAccepted && newAgent)
            {
                IEventQueue eq = sp.Scene.RequestModuleInterface<IEventQueue>();
                if (eq != null)
                {
                    #region IP Translation for NAT
                    IClientIPEndpoint ipepClient;
                    if (sp.ClientView.TryGet(out ipepClient))
                    {
                        endPoint.Address = NetworkUtil.GetIPFor(ipepClient.EndPoint, endPoint.Address);
                    }
                    #endregion

                    //m_log.DebugFormat("[ENTITY TRANSFER MODULE]: {0} is sending {1} EnableSimulator for neighbor region {2} @ {3} " +
                    //    "and EstablishAgentCommunication with seed cap {4}",
                    //    m_scene.RegionInfo.RegionName, sp.Name, reg.RegionName, reg.RegionHandle, capsPath);

                    eq.EnableSimulator(reg.RegionHandle, endPoint, sp.UUID);
                    eq.EstablishAgentCommunication(sp.UUID, endPoint, capsPath);
                }
                else
                {
                    sp.ControllingClient.InformClientOfNeighbour(reg.RegionHandle, endPoint);
                    // TODO: make Event Queue disablable!
                }

                //m_log.Info("[ENTITY TRANSFER MODULE]: Completed inform client about neighbour " + endPoint.ToString());

            }

        }

        protected List<GridRegion> RequestNeighbours(Scene pScene, uint pRegionLocX, uint pRegionLocY)
        {
            RegionInfo m_regionInfo = pScene.RegionInfo;

            Border[] northBorders = pScene.NorthBorders.ToArray();
            Border[] southBorders = pScene.SouthBorders.ToArray();
            Border[] eastBorders = pScene.EastBorders.ToArray();
            Border[] westBorders = pScene.WestBorders.ToArray();

            // Legacy one region.  Provided for simplicity while testing the all inclusive method in the else statement.
            if (northBorders.Length <= 1 && southBorders.Length <= 1 && eastBorders.Length <= 1 && westBorders.Length <= 1)
            {
                return pScene.GridService.GetNeighbours(m_regionInfo.ScopeID, m_regionInfo.RegionID);
            }
            else
            {
                Vector2 extent = Vector2.Zero;
                for (int i = 0; i < eastBorders.Length; i++)
                {
                    extent.X = (eastBorders[i].BorderLine.Z > extent.X) ? eastBorders[i].BorderLine.Z : extent.X;
                }
                for (int i = 0; i < northBorders.Length; i++)
                {
                    extent.Y = (northBorders[i].BorderLine.Z > extent.Y) ? northBorders[i].BorderLine.Z : extent.Y;
                }

                // Loss of fraction on purpose
                extent.X = ((int)extent.X / (int)Constants.RegionSize) + 1;
                extent.Y = ((int)extent.Y / (int)Constants.RegionSize) + 1;

                int startX = (int)(pRegionLocX - 1) * (int)Constants.RegionSize;
                int startY = (int)(pRegionLocY - 1) * (int)Constants.RegionSize;

                int endX = ((int)pRegionLocX + (int)extent.X) * (int)Constants.RegionSize;
                int endY = ((int)pRegionLocY + (int)extent.Y) * (int)Constants.RegionSize;

                List<GridRegion> neighbours = pScene.GridService.GetRegionRange(m_regionInfo.ScopeID, startX, endX, startY, endY);
                neighbours.RemoveAll(delegate(GridRegion r) { return r.RegionID == m_regionInfo.RegionID; });

                return neighbours;
            }
        }

        private List<ulong> NewNeighbours(List<ulong> currentNeighbours, List<ulong> previousNeighbours)
        {
            return currentNeighbours.FindAll(delegate(ulong handle) { return !previousNeighbours.Contains(handle); });
        }

        //        private List<ulong> CommonNeighbours(List<ulong> currentNeighbours, List<ulong> previousNeighbours)
        //        {
        //            return currentNeighbours.FindAll(delegate(ulong handle) { return previousNeighbours.Contains(handle); });
        //        }

        private List<ulong> OldNeighbours(List<ulong> currentNeighbours, List<ulong> previousNeighbours)
        {
            return previousNeighbours.FindAll(delegate(ulong handle) { return !currentNeighbours.Contains(handle); });
        }

        private List<ulong> NeighbourHandles(List<GridRegion> neighbours)
        {
            List<ulong> handles = new List<ulong>();
            foreach (GridRegion reg in neighbours)
            {
                handles.Add(reg.RegionHandle);
            }
            return handles;
        }

        private void Dump(string msg, List<ulong> handles)
        {
            m_log.InfoFormat("-------------- HANDLE DUMP ({0}) ---------", msg);
            foreach (ulong handle in handles)
            {
                uint x, y;
                Utils.LongToUInts(handle, out x, out y);
                x = x / Constants.RegionSize;
                y = y / Constants.RegionSize;
                m_log.InfoFormat("({0}, {1})", x, y);
            }
        }

        #endregion


        #region Agent Arrived
        public void AgentArrivedAtDestination(UUID id)
        {
            //m_log.Debug(" >>> ReleaseAgent called <<< ");
            ResetFromTransit(id);
        }

        #endregion

        #region Object Transfers
        /// <summary>
        /// Move the given scene object into a new region depending on which region its absolute position has moved
        /// into.
        ///
        /// This method locates the new region handle and offsets the prim position for the new region
        /// </summary>
        /// <param name="attemptedPosition">the attempted out of region position of the scene object</param>
        /// <param name="grp">the scene object that we're crossing</param>
        public void Cross(SceneObjectGroup grp, Vector3 attemptedPosition, bool silent)
        {
            if (grp == null)
                return;
            if (grp.IsDeleted)
                return;

            Scene scene = grp.Scene;
            if (scene == null)
                return;
            if (grp.RootPart.DIE_AT_EDGE)
            {
                // We remove the object here
                try
                {
                    scene.DeleteSceneObject(grp, false, true);
                }
                catch (Exception)
                {
                    m_log.Warn("[DATABASE]: exception when trying to remove the prim that crossed the border.");
                }
                return;
            }

            int thisx = (int)scene.RegionInfo.RegionLocX;
            int thisy = (int)scene.RegionInfo.RegionLocY;
            Vector3 EastCross = new Vector3(0.1f, 0, 0);
            Vector3 WestCross = new Vector3(-0.1f, 0, 0);
            Vector3 NorthCross = new Vector3(0, 0.1f, 0);
            Vector3 SouthCross = new Vector3(0, -0.1f, 0);


            // use this if no borders were crossed!
            ulong newRegionHandle
                        = Util.UIntsToLong((uint)((thisx) * Constants.RegionSize),
                                           (uint)((thisy) * Constants.RegionSize));

            Vector3 pos = attemptedPosition;

            int changeX = 1;
            int changeY = 1;

            if (scene.TestBorderCross(attemptedPosition + WestCross, Cardinals.W))
            {
                if (scene.TestBorderCross(attemptedPosition + SouthCross, Cardinals.S))
                {

                    Border crossedBorderx = scene.GetCrossedBorder(attemptedPosition + WestCross, Cardinals.W);

                    if (crossedBorderx.BorderLine.Z > 0)
                    {
                        pos.X = ((pos.X + crossedBorderx.BorderLine.Z));
                        changeX = (int)(crossedBorderx.BorderLine.Z / (int)Constants.RegionSize);
                    }
                    else
                        pos.X = ((pos.X + Constants.RegionSize));

                    Border crossedBordery = scene.GetCrossedBorder(attemptedPosition + SouthCross, Cardinals.S);
                    //(crossedBorderx.BorderLine.Z / (int)Constants.RegionSize)

                    if (crossedBordery.BorderLine.Z > 0)
                    {
                        pos.Y = ((pos.Y + crossedBordery.BorderLine.Z));
                        changeY = (int)(crossedBordery.BorderLine.Z / (int)Constants.RegionSize);
                    }
                    else
                        pos.Y = ((pos.Y + Constants.RegionSize));



                    newRegionHandle
                        = Util.UIntsToLong((uint)((thisx - changeX) * Constants.RegionSize),
                                           (uint)((thisy - changeY) * Constants.RegionSize));
                    // x - 1
                    // y - 1
                }
                else if (scene.TestBorderCross(attemptedPosition + NorthCross, Cardinals.N))
                {
                    Border crossedBorderx = scene.GetCrossedBorder(attemptedPosition + WestCross, Cardinals.W);

                    if (crossedBorderx.BorderLine.Z > 0)
                    {
                        pos.X = ((pos.X + crossedBorderx.BorderLine.Z));
                        changeX = (int)(crossedBorderx.BorderLine.Z / (int)Constants.RegionSize);
                    }
                    else
                        pos.X = ((pos.X + Constants.RegionSize));


                    Border crossedBordery = scene.GetCrossedBorder(attemptedPosition + SouthCross, Cardinals.S);
                    //(crossedBorderx.BorderLine.Z / (int)Constants.RegionSize)

                    if (crossedBordery.BorderLine.Z > 0)
                    {
                        pos.Y = ((pos.Y + crossedBordery.BorderLine.Z));
                        changeY = (int)(crossedBordery.BorderLine.Z / (int)Constants.RegionSize);
                    }
                    else
                        pos.Y = ((pos.Y + Constants.RegionSize));

                    newRegionHandle
                        = Util.UIntsToLong((uint)((thisx - changeX) * Constants.RegionSize),
                                           (uint)((thisy + changeY) * Constants.RegionSize));
                    // x - 1
                    // y + 1
                }
                else
                {
                    Border crossedBorderx = scene.GetCrossedBorder(attemptedPosition + WestCross, Cardinals.W);

                    if (crossedBorderx.BorderLine.Z > 0)
                    {
                        pos.X = ((pos.X + crossedBorderx.BorderLine.Z));
                        changeX = (int)(crossedBorderx.BorderLine.Z / (int)Constants.RegionSize);
                    }
                    else
                        pos.X = ((pos.X + Constants.RegionSize));

                    newRegionHandle
                        = Util.UIntsToLong((uint)((thisx - changeX) * Constants.RegionSize),
                                           (uint)(thisy * Constants.RegionSize));
                    // x - 1
                }
            }
            else if (scene.TestBorderCross(attemptedPosition + EastCross, Cardinals.E))
            {
                if (scene.TestBorderCross(attemptedPosition + SouthCross, Cardinals.S))
                {

                    pos.X = ((pos.X - Constants.RegionSize));
                    Border crossedBordery = scene.GetCrossedBorder(attemptedPosition + SouthCross, Cardinals.S);
                    //(crossedBorderx.BorderLine.Z / (int)Constants.RegionSize)

                    if (crossedBordery.BorderLine.Z > 0)
                    {
                        pos.Y = ((pos.Y + crossedBordery.BorderLine.Z));
                        changeY = (int)(crossedBordery.BorderLine.Z / (int)Constants.RegionSize);
                    }
                    else
                        pos.Y = ((pos.Y + Constants.RegionSize));


                    newRegionHandle
                        = Util.UIntsToLong((uint)((thisx + changeX) * Constants.RegionSize),
                                           (uint)((thisy - changeY) * Constants.RegionSize));
                    // x + 1
                    // y - 1
                }
                else if (scene.TestBorderCross(attemptedPosition + NorthCross, Cardinals.N))
                {
                    pos.X = ((pos.X - Constants.RegionSize));
                    pos.Y = ((pos.Y - Constants.RegionSize));
                    newRegionHandle
                        = Util.UIntsToLong((uint)((thisx + changeX) * Constants.RegionSize),
                                           (uint)((thisy + changeY) * Constants.RegionSize));
                    // x + 1
                    // y + 1
                }
                else
                {
                    pos.X = ((pos.X - Constants.RegionSize));
                    newRegionHandle
                        = Util.UIntsToLong((uint)((thisx + changeX) * Constants.RegionSize),
                                           (uint)(thisy * Constants.RegionSize));
                    // x + 1
                }
            }
            else if (scene.TestBorderCross(attemptedPosition + SouthCross, Cardinals.S))
            {
                Border crossedBordery = scene.GetCrossedBorder(attemptedPosition + SouthCross, Cardinals.S);
                //(crossedBorderx.BorderLine.Z / (int)Constants.RegionSize)

                if (crossedBordery.BorderLine.Z > 0)
                {
                    pos.Y = ((pos.Y + crossedBordery.BorderLine.Z));
                    changeY = (int)(crossedBordery.BorderLine.Z / (int)Constants.RegionSize);
                }
                else
                    pos.Y = ((pos.Y + Constants.RegionSize));

                newRegionHandle
                    = Util.UIntsToLong((uint)(thisx * Constants.RegionSize), (uint)((thisy - changeY) * Constants.RegionSize));
                // y - 1
            }
            else if (scene.TestBorderCross(attemptedPosition + NorthCross, Cardinals.N))
            {

                pos.Y = ((pos.Y - Constants.RegionSize));
                newRegionHandle
                    = Util.UIntsToLong((uint)(thisx * Constants.RegionSize), (uint)((thisy + changeY) * Constants.RegionSize));
                // y + 1
            }

            // Offset the positions for the new region across the border
            Vector3 oldGroupPosition = grp.RootPart.GroupPosition;
            grp.OffsetForNewRegion(pos);

            // If we fail to cross the border, then reset the position of the scene object on that border.
            uint x = 0, y = 0;
            Utils.LongToUInts(newRegionHandle, out x, out y);
            GridRegion destination = scene.GridService.GetRegionByPosition(UUID.Zero, (int)x, (int)y);
            if (destination != null && !CrossPrimGroupIntoNewRegion(destination, grp, silent))
            {
                grp.OffsetForNewRegion(oldGroupPosition);
                grp.ScheduleGroupForFullUpdate(PrimUpdateFlags.FullUpdate);
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
        protected bool CrossPrimGroupIntoNewRegion(GridRegion destination, SceneObjectGroup grp, bool silent)
        {
            bool successYN = false;
            grp.RootPart.ClearUpdateSchedule();

            if (destination != null)
            {
                if (grp.RootPart.SitTargetAvatar.Count != 0)
                {
                    lock (grp.RootPart.SitTargetAvatar)
                    {
                        foreach (UUID avID in grp.RootPart.SitTargetAvatar)
                        {
                            ScenePresence SP = grp.Scene.GetScenePresence(avID);
                            CrossAgentSittingToNewRegionAsync(SP, destination, grp);
                        }
                    }
                }

                if (m_aScene.SimulationService != null)
                    successYN = m_aScene.SimulationService.CreateObject(destination, grp, true);

                if (successYN)
                {
                    // We remove the object here
                    try
                    {
                        foreach (SceneObjectPart part in grp.ChildrenList)
                        {
                            lock (part.SitTargetAvatar)
                            {
                                part.SitTargetAvatar.Clear();
                            }
                        }
                        grp.Scene.DeleteSceneObject(grp, silent, false);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[ENTITY TRANSFER MODULE]: Exception deleting the old object left behind on a border crossing for {0}, {1}",
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

                    m_log.ErrorFormat("[ENTITY TRANSFER MODULE]: Prim crossing failed for {0}", grp);
                }
            }
            else
            {
                m_log.Error("[ENTITY TRANSFER MODULE]: destination was unexpectedly null in Scene.CrossPrimGroupIntoNewRegion()");
            }

            return successYN;
        }

        protected bool CrossAttachmentsIntoNewRegion(GridRegion destination, ScenePresence sp, bool silent)
        {
            List<SceneObjectGroup> m_attachments = sp.Attachments;
            lock (m_attachments)
            {
                // Validate
                foreach (SceneObjectGroup gobj in m_attachments)
                {
                    if (gobj == null || gobj.IsDeleted)
                        return false;
                }

                foreach (SceneObjectGroup gobj in m_attachments)
                {
                    // If the prim group is null then something must have happened to it!
                    if (gobj != null && gobj.RootPart != null)
                    {
                        // Set the parent localID to 0 so it transfers over properly.
                        gobj.RootPart.SetParentLocalId(0);
                        gobj.AbsolutePosition = gobj.RootPart.AttachedPos;
                        gobj.RootPart.IsAttachment = false;
                        //gobj.RootPart.LastOwnerID = gobj.GetFromAssetID();
                        //m_log.DebugFormat("[ENTITY TRANSFER MODULE]: Sending attachment {0} to region {1}", gobj.UUID, destination.RegionName);
                        CrossPrimGroupIntoNewRegion(destination, gobj, silent);
                    }
                }
                m_attachments.Clear();

                return true;
            }
        }

        #endregion

        #region Misc

        protected bool WaitForCallback(UUID id)
        {
            int count = 200;
            while (m_agentsInTransit.Contains(id) && count-- > 0)
            {
                //m_log.Debug("  >>> Waiting... " + count);
                Thread.Sleep(100);
            }

            if (count > 0)
                return true;
            else
                return false;
        }

        protected void SetInTransit(UUID id)
        {
            lock (m_agentsInTransit)
            {
                if (!m_agentsInTransit.Contains(id))
                    m_agentsInTransit.Add(id);
            }
        }

        protected bool ResetFromTransit(UUID id)
        {
            lock (m_agentsInTransit)
            {
                if (m_agentsInTransit.Contains(id))
                {
                    m_agentsInTransit.Remove(id);
                    return true;
                }
            }
            return false;
        }

        public void CancelTeleport(UUID AgentID)
        {
            m_cancelingAgents.Add(AgentID);
        }


        #endregion

    }
}
