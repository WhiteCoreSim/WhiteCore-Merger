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
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace OpenSim.Framework.Communications.Services
{
    /// <summary>
    /// A temp class to handle login response.
    /// Should make use of UserProfileManager where possible.
    /// </summary>
    public class LoginResponse
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Hashtable loginFlagsHash;
        private Hashtable globalTexturesHash;
        private Hashtable loginError;
        private Hashtable uiConfigHash;

        private ArrayList loginFlags;
        private ArrayList globalTextures;
        private ArrayList eventCategories;
        private ArrayList uiConfig;
        private ArrayList classifiedCategories;
        private ArrayList inventoryRoot;
        private ArrayList initialOutfit;
        private ArrayList agentInventory;
        private ArrayList inventoryLibraryOwner;
        private ArrayList inventoryLibRoot;
        private ArrayList inventoryLibrary;
        private ArrayList activeGestures;

        private UserInfo userProfile;

        private UUID agentID;
        private UUID sessionID;
        private UUID secureSessionID;

        // Login Flags
        private string dst;
        private string stipendSinceLogin;
        private string gendered;
        private string everLoggedIn;
        private string login;
        private uint simPort;
        private uint simHttpPort;
        private string simAddress;
        private string agentAccess;
        private string agentAccessMax;
        private Int32 circuitCode;
        private uint regionX;
        private uint regionY;

        // Login
        private string firstname;
        private string lastname;

        // Global Textures
        private string sunTexture;
        private string cloudTexture;
        private string moonTexture;

        // Error Flags
        private string errorReason;
        private string errorMessage;

        // Response
        private XmlRpcResponse xmlRpcResponse;
        // private XmlRpcResponse defaultXmlRpcResponse;

        private string welcomeMessage;
        private string startLocation;
        private string allowFirstLife;
        private string home;
        private string seedCapability;
        private string lookAt;

        private BuddyList m_buddyList = null;

        public LoginResponse()
        {
            loginFlags = new ArrayList();
            globalTextures = new ArrayList();
            eventCategories = new ArrayList();
            uiConfig = new ArrayList();
            classifiedCategories = new ArrayList();

            loginError = new Hashtable();
            uiConfigHash = new Hashtable();

            // defaultXmlRpcResponse = new XmlRpcResponse();
            userProfile = new UserInfo();
            inventoryRoot = new ArrayList();
            initialOutfit = new ArrayList();
            agentInventory = new ArrayList();
            inventoryLibrary = new ArrayList();
            inventoryLibraryOwner = new ArrayList();
            activeGestures = new ArrayList();

            xmlRpcResponse = new XmlRpcResponse();
            // defaultXmlRpcResponse = new XmlRpcResponse();

            SetDefaultValues();
        }

        private void SetDefaultValues()
        {
            DST = TimeZone.CurrentTimeZone.IsDaylightSavingTime(DateTime.Now) ? "Y" : "N";
            StipendSinceLogin = "N";
            Gendered = "Y";
            EverLoggedIn = "Y";
            login = "false";
            firstname = "Test";
            lastname = "User";
            agentAccess = "M";
            agentAccessMax = "A";
            startLocation = "last";
            allowFirstLife = "Y";

            SunTexture = "cce0f112-878f-4586-a2e2-a8f104bba271";
            CloudTexture = "dc4b9f0b-d008-45c6-96a4-01dd947ac621";
            MoonTexture = "ec4b9f0b-d008-45c6-96a4-01dd947ac621";

            ErrorMessage = "You have entered an invalid name/password combination.  Check Caps/lock.";
            ErrorReason = "key";
            welcomeMessage = "Welcome to OpenSim!";
            seedCapability = String.Empty;
            home = "{'region_handle':[r" + (1000*Constants.RegionSize).ToString() + ",r" + (1000*Constants.RegionSize).ToString() + "], 'position':[r" +
                   userProfile.homepos.X.ToString() + ",r" + userProfile.homepos.Y.ToString() + ",r" +
                   userProfile.homepos.Z.ToString() + "], 'look_at':[r" + userProfile.homelookat.X.ToString() + ",r" +
                   userProfile.homelookat.Y.ToString() + ",r" + userProfile.homelookat.Z.ToString() + "]}";
            lookAt = "[r0.99949799999999999756,r0.03166859999999999814,r0]";
            RegionX = (uint) 255232;
            RegionY = (uint) 254976;

            // Classifieds;
            AddClassifiedCategory((Int32) 1, "Shopping");
            AddClassifiedCategory((Int32) 2, "Land Rental");
            AddClassifiedCategory((Int32) 3, "Property Rental");
            AddClassifiedCategory((Int32) 4, "Special Attraction");
            AddClassifiedCategory((Int32) 5, "New Products");
            AddClassifiedCategory((Int32) 6, "Employment");
            AddClassifiedCategory((Int32) 7, "Wanted");
            AddClassifiedCategory((Int32) 8, "Service");
            AddClassifiedCategory((Int32) 9, "Personal");

            SessionID = UUID.Random();
            SecureSessionID = UUID.Random();
            AgentID = UUID.Random();

            Hashtable InitialOutfitHash = new Hashtable();
            InitialOutfitHash["folder_name"] = "Nightclub Female";
            InitialOutfitHash["gender"] = "female";
            initialOutfit.Add(InitialOutfitHash);
        }

        #region Login Failure Methods

        public XmlRpcResponse GenerateFailureResponse(string reason, string message, string login)
        {
            // Overwrite any default values;
            xmlRpcResponse = new XmlRpcResponse();

            // Ensure Login Failed message/reason;
            ErrorMessage = message;
            ErrorReason = reason;

            loginError["reason"] = ErrorReason;
            loginError["message"] = ErrorMessage;
            loginError["login"] = login;
            xmlRpcResponse.Value = loginError;
            return (xmlRpcResponse);
        }

        public OSD GenerateFailureResponseLLSD(string reason, string message, string login)
        {
            OSDMap map = new OSDMap();

            // Ensure Login Failed message/reason;
            ErrorMessage = message;
            ErrorReason = reason;

            map["reason"] = OSD.FromString(ErrorReason);
            map["message"] = OSD.FromString(ErrorMessage);
            map["login"] = OSD.FromString(login);

            return map;
        }

        public XmlRpcResponse CreateFailedResponse()
        {
            return (CreateLoginFailedResponse());
        }

        public OSD CreateFailedResponseLLSD()
        {
            return CreateLoginFailedResponseLLSD();
        }

        public XmlRpcResponse CreateLoginFailedResponse()
        {
            return
                (GenerateFailureResponse("key",
                                         "Could not authenticate your avatar. Please check your username and password, and check the grid if problems persist.",
                                         "false"));
        }

        public OSD CreateLoginFailedResponseLLSD()
        {
            return GenerateFailureResponseLLSD(
                "key",
                "Could not authenticate your avatar. Please check your username and password, and check the grid if problems persist.",
                "false");
        }

        /// <summary>
        /// Response to indicate that login failed because the agent's inventory was not available.
        /// </summary>
        /// <returns></returns>
        public XmlRpcResponse CreateLoginInventoryFailedResponse()
        {
            return GenerateFailureResponse(
                "key",
                "The avatar inventory service is not responding.  Please notify your login region operator.",
                "false");
        }

        public XmlRpcResponse CreateAlreadyLoggedInResponse()
        {
            return
                (GenerateFailureResponse("presence",
                                         "You appear to be already logged in. " +
                                         "If this is not the case please wait for your session to timeout. " +
                                         "If this takes longer than a few minutes please contact the grid owner. " +
                                         "Please wait 5 minutes if you are going to connect to a region nearby to the region you were at previously.",
                                         "false"));
        }

        public OSD CreateAlreadyLoggedInResponseLLSD()
        {
            return GenerateFailureResponseLLSD(
                "presence",
                "You appear to be already logged in. " +
                "If this is not the case please wait for your session to timeout. " +
                "If this takes longer than a few minutes please contact the grid owner",
                "false");
        }

        public XmlRpcResponse CreateLoginBlockedResponse()
        {
            return
                (GenerateFailureResponse("presence",
                "Logins are currently restricted. Please try again later",
                                         "false"));
        }

        public OSD CreateLoginBlockedResponseLLSD()
        {
            return GenerateFailureResponseLLSD(
                "presence",
                "Logins are currently restricted. Please try again later",
                "false");
        }

        public XmlRpcResponse CreateDeadRegionResponse()
        {
            return
                (GenerateFailureResponse("key",
                                         "The region you are attempting to log into is not responding. Please select another region and try again.",
                                         "false"));
        }

        public OSD CreateDeadRegionResponseLLSD()
        {
            return GenerateFailureResponseLLSD(
                "key",
                "The region you are attempting to log into is not responding. Please select another region and try again.",
                "false");
        }

        public XmlRpcResponse CreateGridErrorResponse()
        {
            return
                (GenerateFailureResponse("key",
                                         "Error connecting to grid. Could not percieve credentials from login XML.",
                                         "false"));
        }

        public OSD CreateGridErrorResponseLLSD()
        {
            return GenerateFailureResponseLLSD(
                "key",
                "Error connecting to grid. Could not perceive credentials from login XML.",
                "false");
        }

        #endregion

        public virtual XmlRpcResponse ToXmlRpcResponse()
        {
            try
            {
                Hashtable responseData = new Hashtable();

                loginFlagsHash = new Hashtable();
                loginFlagsHash["daylight_savings"] = DST;
                loginFlagsHash["stipend_since_login"] = StipendSinceLogin;
                loginFlagsHash["gendered"] = Gendered;
                loginFlagsHash["ever_logged_in"] = EverLoggedIn;
                loginFlags.Add(loginFlagsHash);

                responseData["first_name"] = Firstname;
                responseData["last_name"] = Lastname;
                responseData["agent_access"] = agentAccess;
                responseData["agent_access_max"] = agentAccessMax;

                globalTexturesHash = new Hashtable();
                globalTexturesHash["sun_texture_id"] = SunTexture;
                globalTexturesHash["cloud_texture_id"] = CloudTexture;
                globalTexturesHash["moon_texture_id"] = MoonTexture;
                globalTextures.Add(globalTexturesHash);
                // this.eventCategories.Add(this.eventCategoriesHash);

                AddToUIConfig("allow_first_life", allowFirstLife);
                uiConfig.Add(uiConfigHash);

                responseData["sim_port"] = (Int32) SimPort;
                responseData["sim_ip"] = SimAddress;
                responseData["http_port"] = (Int32)SimHttpPort;

                responseData["agent_id"] = AgentID.ToString();
                responseData["session_id"] = SessionID.ToString();
                responseData["secure_session_id"] = SecureSessionID.ToString();
                responseData["circuit_code"] = CircuitCode;
                responseData["seconds_since_epoch"] = (Int32) (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                responseData["login-flags"] = loginFlags;
                responseData["global-textures"] = globalTextures;
                responseData["seed_capability"] = seedCapability;

                responseData["event_categories"] = eventCategories;
                responseData["event_notifications"] = new ArrayList(); // todo
                responseData["classified_categories"] = classifiedCategories;
                responseData["ui-config"] = uiConfig;

                if (agentInventory != null)
                {
                    responseData["inventory-skeleton"] = agentInventory;
                    responseData["inventory-root"] = inventoryRoot;
                }
                responseData["inventory-skel-lib"] = inventoryLibrary;
                responseData["inventory-lib-root"] = inventoryLibRoot;
                responseData["gestures"] = activeGestures;
                responseData["inventory-lib-owner"] = inventoryLibraryOwner;
                responseData["initial-outfit"] = initialOutfit;
                responseData["start_location"] = startLocation;
                responseData["seed_capability"] = seedCapability;
                responseData["home"] = home;
                responseData["look_at"] = lookAt;
                responseData["message"] = welcomeMessage;
                responseData["region_x"] = (Int32)(RegionX * Constants.RegionSize);
                responseData["region_y"] = (Int32)(RegionY * Constants.RegionSize);

                if (m_buddyList != null)
                {
                    responseData["buddy-list"] = m_buddyList.ToArray();
                }

                responseData["login"] = "true";
                xmlRpcResponse.Value = responseData;

                return (xmlRpcResponse);
            }
            catch (Exception e)
            {
                m_log.Warn("[CLIENT]: LoginResponse: Error creating XML-RPC Response: " + e.Message);

                return (GenerateFailureResponse("Internal Error", "Error generating Login Response", "false"));
            }
        }

        public OSD ToLLSDResponse()
        {
            try
            {
                OSDMap map = new OSDMap();

                map["first_name"] = OSD.FromString(Firstname);
                map["last_name"] = OSD.FromString(Lastname);
                map["agent_access"] = OSD.FromString(agentAccess);
                map["agent_access_max"] = OSD.FromString(agentAccessMax);

                map["sim_port"] = OSD.FromInteger(SimPort);
                map["sim_ip"] = OSD.FromString(SimAddress);

                map["agent_id"] = OSD.FromUUID(AgentID);
                map["session_id"] = OSD.FromUUID(SessionID);
                map["secure_session_id"] = OSD.FromUUID(SecureSessionID);
                map["circuit_code"] = OSD.FromInteger(CircuitCode);
                map["seconds_since_epoch"] = OSD.FromInteger((int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds);

                #region Login Flags

                OSDMap loginFlagsLLSD = new OSDMap();
                loginFlagsLLSD["daylight_savings"] = OSD.FromString(DST);
                loginFlagsLLSD["stipend_since_login"] = OSD.FromString(StipendSinceLogin);
                loginFlagsLLSD["gendered"] = OSD.FromString(Gendered);
                loginFlagsLLSD["ever_logged_in"] = OSD.FromString(EverLoggedIn);
                map["login-flags"] = WrapOSDMap(loginFlagsLLSD);

                #endregion Login Flags

                #region Global Textures

                OSDMap globalTexturesLLSD = new OSDMap();
                globalTexturesLLSD["sun_texture_id"] = OSD.FromString(SunTexture);
                globalTexturesLLSD["cloud_texture_id"] = OSD.FromString(CloudTexture);
                globalTexturesLLSD["moon_texture_id"] = OSD.FromString(MoonTexture);

                map["global-textures"] = WrapOSDMap(globalTexturesLLSD);

                #endregion Global Textures

                map["seed_capability"] = OSD.FromString(seedCapability);

                map["event_categories"] = ArrayListToOSDArray(eventCategories);
                //map["event_notifications"] = new OSDArray(); // todo
                map["classified_categories"] = ArrayListToOSDArray(classifiedCategories);

                #region UI Config

                OSDMap uiConfigLLSD = new OSDMap();
                uiConfigLLSD["allow_first_life"] = OSD.FromString(allowFirstLife);
                map["ui-config"] = WrapOSDMap(uiConfigLLSD);

                #endregion UI Config

                #region Inventory

                map["inventory-skeleton"] = ArrayListToOSDArray(agentInventory);

                map["inventory-skel-lib"] = ArrayListToOSDArray(inventoryLibrary);
                map["inventory-root"] = ArrayListToOSDArray(inventoryRoot); ;
                map["inventory-lib-root"] = ArrayListToOSDArray(inventoryLibRoot);
                map["inventory-lib-owner"] = ArrayListToOSDArray(inventoryLibraryOwner);

                #endregion Inventory

                map["gestures"] = ArrayListToOSDArray(activeGestures);

                map["initial-outfit"] = ArrayListToOSDArray(initialOutfit);
                map["start_location"] = OSD.FromString(startLocation);

                map["seed_capability"] = OSD.FromString(seedCapability);
                map["home"] = OSD.FromString(home);
                map["look_at"] = OSD.FromString(lookAt);
                map["message"] = OSD.FromString(welcomeMessage);
                map["region_x"] = OSD.FromInteger(RegionX * Constants.RegionSize);
                map["region_y"] = OSD.FromInteger(RegionY * Constants.RegionSize);

                if (m_buddyList != null)
                {
                    map["buddy-list"] = ArrayListToOSDArray(m_buddyList.ToArray());
                }

                map["login"] = OSD.FromString("true");

                return map;
            }
            catch (Exception e)
            {
                m_log.Warn("[CLIENT]: LoginResponse: Error creating LLSD Response: " + e.Message);

                return GenerateFailureResponseLLSD("Internal Error", "Error generating Login Response", "false");
            }
        }

        public OSDArray ArrayListToOSDArray(ArrayList arrlst)
        {
            OSDArray llsdBack = new OSDArray();
            foreach (Hashtable ht in arrlst)
            {
                OSDMap mp = new OSDMap();
                foreach (DictionaryEntry deHt in ht)
                {
                    mp.Add((string)deHt.Key, OSDString.FromObject(deHt.Value));
                }
                llsdBack.Add(mp);
            }
            return llsdBack;
        }

        private static OSDArray WrapOSDMap(OSDMap wrapMe)
        {
            OSDArray array = new OSDArray();
            array.Add(wrapMe);
            return array;
        }

        public void SetEventCategories(string category, string value)
        {
            //  this.eventCategoriesHash[category] = value;
            //TODO
        }

        public void AddToUIConfig(string itemName, string item)
        {
            uiConfigHash[itemName] = item;
        }

        public void AddClassifiedCategory(Int32 ID, string categoryName)
        {
            Hashtable hash = new Hashtable();
            hash["category_name"] = categoryName;
            hash["category_id"] = ID;
            classifiedCategories.Add(hash);
            // this.classifiedCategoriesHash.Clear();
        }

        #region Properties

        public string Login
        {
            get { return login; }
            set { login = value; }
        }

        public string DST
        {
            get { return dst; }
            set { dst = value; }
        }

        public string StipendSinceLogin
        {
            get { return stipendSinceLogin; }
            set { stipendSinceLogin = value; }
        }

        public string Gendered
        {
            get { return gendered; }
            set { gendered = value; }
        }

        public string EverLoggedIn
        {
            get { return everLoggedIn; }
            set { everLoggedIn = value; }
        }

        public uint SimPort
        {
            get { return simPort; }
            set { simPort = value; }
        }

        public uint SimHttpPort
        {
            get { return simHttpPort; }
            set { simHttpPort = value; }
        }

        public string SimAddress
        {
            get { return simAddress; }
            set { simAddress = value; }
        }

        public UUID AgentID
        {
            get { return agentID; }
            set { agentID = value; }
        }

        public UUID SessionID
        {
            get { return sessionID; }
            set { sessionID = value; }
        }

        public UUID SecureSessionID
        {
            get { return secureSessionID; }
            set { secureSessionID = value; }
        }

        public Int32 CircuitCode
        {
            get { return circuitCode; }
            set { circuitCode = value; }
        }

        public uint RegionX
        {
            get { return regionX; }
            set { regionX = value; }
        }

        public uint RegionY
        {
            get { return regionY; }
            set { regionY = value; }
        }

        public string SunTexture
        {
            get { return sunTexture; }
            set { sunTexture = value; }
        }

        public string CloudTexture
        {
            get { return cloudTexture; }
            set { cloudTexture = value; }
        }

        public string MoonTexture
        {
            get { return moonTexture; }
            set { moonTexture = value; }
        }

        public string Firstname
        {
            get { return firstname; }
            set { firstname = value; }
        }

        public string Lastname
        {
            get { return lastname; }
            set { lastname = value; }
        }

        public string AgentAccess
        {
            get { return agentAccess; }
            set { agentAccess = value; }
        }

        public string AgentAccessMax
        {
            get { return agentAccessMax; }
            set { agentAccessMax = value; }
        }

        public string StartLocation
        {
            get { return startLocation; }
            set { startLocation = value; }
        }

        public string LookAt
        {
            get { return lookAt; }
            set { lookAt = value; }
        }

        public string SeedCapability
        {
            get { return seedCapability; }
            set { seedCapability = value; }
        }

        public string ErrorReason
        {
            get { return errorReason; }
            set { errorReason = value; }
        }

        public string ErrorMessage
        {
            get { return errorMessage; }
            set { errorMessage = value; }
        }

        public ArrayList InventoryRoot
        {
            get { return inventoryRoot; }
            set { inventoryRoot = value; }
        }

        public ArrayList InventorySkeleton
        {
            get { return agentInventory; }
            set { agentInventory = value; }
        }

        public ArrayList InventoryLibrary
        {
            get { return inventoryLibrary; }
            set { inventoryLibrary = value; }
        }

        public ArrayList InventoryLibraryOwner
        {
            get { return inventoryLibraryOwner; }
            set { inventoryLibraryOwner = value; }
        }

        public ArrayList InventoryLibRoot
        {
            get { return inventoryLibRoot; }
            set { inventoryLibRoot = value; }
        }

        public ArrayList ActiveGestures
        {
            get { return activeGestures; }
            set { activeGestures = value; }
        }
                
        public string Home
        {
            get { return home; }
            set { home = value; }
        }

        public string Message
        {
            get { return welcomeMessage; }
            set { welcomeMessage = value; }
        }

        public BuddyList BuddList
        {
            get { return m_buddyList; }
            set { m_buddyList = value; }
        }

        #endregion

        public class UserInfo
        {
            public string firstname;
            public string lastname;
            public ulong homeregionhandle;
            public Vector3 homepos;
            public Vector3 homelookat;
        }

        public class BuddyList
        {
            public List<BuddyInfo> Buddies = new List<BuddyInfo>();

            public void AddNewBuddy(BuddyInfo buddy)
            {
                if (!Buddies.Contains(buddy))
                {
                    Buddies.Add(buddy);
                }
            }

            public ArrayList ToArray()
            {
                ArrayList buddyArray = new ArrayList();
                foreach (BuddyInfo buddy in Buddies)
                {
                    buddyArray.Add(buddy.ToHashTable());
                }
                return buddyArray;
            }

            public class BuddyInfo
            {
                public int BuddyRightsHave = 1;
                public int BuddyRightsGiven = 1;
                public UUID BuddyID;

                public BuddyInfo(string buddyID)
                {
                    BuddyID = new UUID(buddyID);
                }

                public BuddyInfo(UUID buddyID)
                {
                    BuddyID = buddyID;
                }

                public Hashtable ToHashTable()
                {
                    Hashtable hTable = new Hashtable();
                    hTable["buddy_rights_has"] = BuddyRightsHave;
                    hTable["buddy_rights_given"] = BuddyRightsGiven;
                    hTable["buddy_id"] = BuddyID.ToString();
                    return hTable;
                }
            }
        }
    }
}
