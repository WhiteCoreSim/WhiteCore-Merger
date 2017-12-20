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

using System.Collections.Generic;
using OpenMetaverse;

namespace OpenSim.Framework.Communications
{
    public interface IGridServices
    {
        string gdebugRegionName { get; set; }
        
        /// <summary>
        /// If true, then regions will accept logins from the user service.  If false, then they will not.
        /// </summary>
        bool RegionLoginsEnabled { get; set; }

        /// <summary>
        /// Register a region with the grid service.
        /// </summary>
        /// <param name="regionInfos"> </param>
        /// <returns></returns>
        /// <exception cref="System.Exception">Thrown if region registration failed</exception>
        RegionCommsListener RegisterRegion(RegionInfo regionInfos);

        /// <summary>
        /// Deregister a region with the grid service.
        /// </summary>
        /// <param name="regionInfo"></param>
        /// <returns></returns>
        /// <exception cref="System.Exception">Thrown if region deregistration failed</exception>
        bool DeregisterRegion(RegionInfo regionInfo);   

        /// <summary>
        /// Get information about the regions neighbouring the given co-ordinates.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        List<SimpleRegionInfo> RequestNeighbours(uint x, uint y);

        RegionInfo RequestNeighbourInfo(ulong regionHandle);
        RegionInfo RequestNeighbourInfo(UUID regionID);
        RegionInfo RequestNeighbourInfo(string name);
        RegionInfo RequestNeighbourInfo(string host, uint port);

        RegionInfo RequestClosestRegion(string regionName);
        Dictionary<string, string> GetGridSettings();
        List<MapBlockData> RequestNeighbourMapBlocks(int minX, int minY, int maxX, int maxY);
        // not complete yet, only contains the fields needed for ParcelInfoReqeust
        LandData RequestLandData(ulong regionHandle, uint x, uint y);

        /// <summary>
        /// Get information about regions starting with the provided name. 
        /// </summary>
        /// <param name="name">
        /// The name to match against.
        /// </param>
        /// <param name="maxNumber">
        /// The maximum number of results to return.
        /// </param>
        /// <returns>
        /// A list of <see cref="RegionInfo"/>s of regions with matching name. If the
        /// grid-server couldn't be contacted or returned an error, return null. 
        /// </returns>
        List<RegionInfo> RequestNamedRegions(string name, int maxNumber);
    }
}
