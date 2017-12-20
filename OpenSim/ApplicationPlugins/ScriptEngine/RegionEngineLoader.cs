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
using System.Reflection;
using log4net;
using Nini.Config;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.ScriptEngine.Shared;

namespace OpenSim.ApplicationPlugins.ScriptEngine
{
    public class RegionEngineLoader : IRegionModule
    {
        // This is a region module.
        // This means: Every time a new region is created, a new instance of this module is also created.
        // This module is responsible for starting the script engine for this region.
        public string Name { get { return "SECS.DotNetEngine.Scheduler.RegionLoader"; } }
        public bool IsSharedModule { get { return true; } }

        internal static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private string tempScriptEngineName = "DotNetEngine";
        public IScriptEngine scriptEngine;
        public IConfigSource ConfigSource;
        public IConfig ScriptConfigSource;
        public void Initialise(Scene scene, IConfigSource source)
        {
            // New region is being created
            // Create a new script engine
            // Make sure we have config
            try
            {
                if (ConfigSource.Configs["SECS"] == null)
                    ConfigSource.AddConfig("SECS");
                ScriptConfigSource = ConfigSource.Configs["SECS"];

                // Is SECS enabled?
                if (ScriptConfigSource.GetBoolean("Enabled", false))
                {
                    LoadEngine();
                    if (scriptEngine != null)
                        scriptEngine.Initialise(scene, source);
                }
            }
            catch (NullReferenceException)
            {
            }
        }

        public void PostInitialise()
        {
            if (scriptEngine != null)
                scriptEngine.PostInitialise();
        }

        public void Close()
        {
            try
            {
                if (scriptEngine != null)
                    scriptEngine.Close();
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[{0}] Unable to close engine \"{1}\": {2}", Name, tempScriptEngineName, ex.ToString());
            }
        }

        private void LoadEngine()
        {
            m_log.DebugFormat("[{0}] Loading region script engine engine \"{1}\".", Name, tempScriptEngineName);
            try
            {
                lock (ComponentFactory.scriptEngines)
                {
                    if (!ComponentFactory.scriptEngines.ContainsKey(tempScriptEngineName))
                    {
                        m_log.ErrorFormat("[{0}] Unable to load region script engine: Script engine \"{1}\" does not exist.", Name, tempScriptEngineName);
                    }
                    else
                    {
                        scriptEngine =
                            Activator.CreateInstance(ComponentFactory.scriptEngines[tempScriptEngineName]) as
                            IScriptEngine;
                    }
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[{0}] Internal error loading region script engine \"{1}\": {2}", Name, tempScriptEngineName, ex.ToString());
            }
        }

    }
}
