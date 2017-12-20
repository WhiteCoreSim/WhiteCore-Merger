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

namespace OpenSim
{
    public class VersionInfo
    {
        /// <value>
        /// This is the OpenSim version string.  Change this if you are releasing a new OpenSim version.
        /// </value>        
        public readonly static string Version = "OpenSimulator Server  0.6.5";  // stay with 27 chars (used in regioninfo)
        
        /// <value>
        /// This is the external interface version.  It is separate from the OpenSimulator project version.  
        /// 
        /// This version number should be 
        /// increased by 1 every time a code change makes the previous OpenSimulator revision incompatible
        /// with the new revision.  This will usually be due to interregion or grid facing interface changes.
        /// 
        /// Changes which are compatible with an older revision (e.g. older revisions experience degraded functionality
        /// but not outright failure) do not need a version number increment.
        /// 
        /// Having this version number allows the grid service to reject connections from regions running a version
        /// of the code that is too old. 
        ///   
        /// </value>
        public readonly static int MajorInterfaceVersion = 3;
    }
}
