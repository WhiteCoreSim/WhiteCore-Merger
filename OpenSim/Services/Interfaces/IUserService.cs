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

using System.Collections.Generic;
using OpenMetaverse;

namespace OpenSim.Services.Interfaces
{
    public class UserAccount
    {
        public UserAccount()
        {
        }

        public UserAccount(UUID userID, UUID homeRegionID, float homePositionX,
                float homePositionY, float homePositionZ, float homeLookAtX,
                float homeLookAtY, float homeLookAtZ)
        {
            UserID = userID;
            HomeRegionID = homeRegionID;
            HomePositionX = homePositionX;
            HomePositionY = homePositionY;
            HomePositionZ = homePositionZ;
            HomeLookAtX = homeLookAtX;
            HomeLookAtY = homeLookAtY;
            HomeLookAtZ = homeLookAtZ;
        }

        public string FirstName;
        public string LastName;
        public UUID UserID;
        public UUID ScopeID;

        // For informational purposes only!
        //
        public string HomeRegionName;

        public UUID HomeRegionID;
        public float HomePositionX;
        public float HomePositionY;
        public float HomePositionZ;
        public float HomeLookAtX;
        public float HomeLookAtY;
        public float HomeLookAtZ;

        // These are here because they
        // concern the account rather than
        // the profile. They just happen to
        // be used in the Linden profile as well
        //
        public int GodLevel;
        public int UserFlags;
        public string AccountType;

    };

    public interface IUserAccountService
    {
        UserAccount GetUserAccount(UUID scopeID, UUID userID);
        UserAccount GetUserAccount(UUID scopeID, string FirstName, string LastName);
        // Returns the list of avatars that matches both the search
        // criterion and the scope ID passed
        //
        List<UserAccount> GetUserAccount(UUID scopeID, string query);


        // This will set only the home region portion of the data!
        // Can't be used to set god level, flags, type or change the name!
        //
        bool SetHomePosition(UserAccount data, UUID RegionID, UUID RegionSecret);

        // Update all updatable fields
        //
        bool SetUserAccount(UserAccount data, UUID PrincipalID, string token);
        
        // Creates a user data record
        bool CreateUserAccount(UserAccount data, UUID PrincipalID, string token);
    }
}
