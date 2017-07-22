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
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting.Lifetime;
using OpenMetaverse;
using Nini.Config;
using OpenSim;
using OpenSim.Framework;
using OpenSim.Region.CoreModules.World.LightShare;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using WhiteCore.ScriptEngine.Shared;
using WhiteCore.ScriptEngine.Shared.Api.Plugins;
using WhiteCore.ScriptEngine.Shared.ScriptBase;
using WhiteCore.ScriptEngine.Interfaces;
using WhiteCore.ScriptEngine.Shared.Api.Interfaces;
using OpenSim.Services.Interfaces;

using LSL_Float = WhiteCore.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = WhiteCore.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = WhiteCore.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = WhiteCore.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = WhiteCore.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = WhiteCore.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = WhiteCore.ScriptEngine.Shared.LSL_Types.Vector3;

namespace WhiteCore.ScriptEngine.Shared.Api
{
    [Serializable]
    public class WC_Api : MarshalByRefObject, IWC_Api, IScriptApi
    {
        internal IScriptEngine m_ScriptEngine;
        internal SceneObjectPart m_host;
        internal TaskInventoryItem m_item;
        internal bool m_WCFunctionsEnabled = false;

        public void Initialize(IScriptEngine ScriptEngine, SceneObjectPart host, TaskInventoryItem item)
        {
            m_ScriptEngine = ScriptEngine;
            m_host = host;
            m_item = item;

            if (m_ScriptEngine.Config.GetBoolean("AllowWhiteCoreFunctions", false))
                m_WCFunctionsEnabled = true;
        }

        public override Object InitializeLifetimeService()
        {
            ILease lease = (ILease)base.InitializeLifetimeService();

            if (lease.CurrentState == LeaseState.Initial)
            {
                lease.InitialLeaseTime = TimeSpan.FromMinutes(0);
                //lease.RenewOnCallTime = TimeSpan.FromSeconds(10.0);
                //lease.SponsorshipTimeout = TimeSpan.FromMinutes(1.0);
            }

            return lease;
        }

        public Scene World
        {
            get { return m_ScriptEngine.World; }
        }

        public string wcDetectedCountry(int number)
        {
            m_host.AddScriptLPS(1);

            if (!m_WCFunctionsEnabled)
                return String.Empty;

            if (World.UserAccountService == null)
                return String.Empty;

            DetectParams detectedParams = m_ScriptEngine.GetDetectParams(m_item.ItemID, number);

            if (detectedParams == null)
                return String.Empty;

            UUID key = detectedParams.Key;

            if (key == UUID.Zero)
                return String.Empty;

            UserAccount account = World.UserAccountService.GetUserAccount(World.RegionInfo.ScopeID, key);

            return account.UserCountry;
        }

        public string wcGetAgentCountry(LSL_Key key)
        {
            if(! m_WCFunctionsEnabled)
                return "";

            if (World.UserAccountService == null)
                return String.Empty;

            if (!World.Permissions.IsGod(m_host.OwnerID))
                return String.Empty;

            UUID uuid;

            if (!UUID.TryParse(key, out uuid))
                return String.Empty;

            UserAccount account = World.UserAccountService.GetUserAccount(World.RegionInfo.ScopeID, uuid);
            return account.UserCountry;
        }
    }
}