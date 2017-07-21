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
using System.Xml;
using System.Xml.Serialization;

using OpenMetaverse;

namespace OpenSim.Framework
{
    /// <summary>
    /// Details of a Parcel of land
    /// </summary>
    public class LandData
    {
        // use only one serializer to give the runtime a chance to
        // optimize it (it won't do that if you use a new instance
        // every time)
        private static XmlSerializer serializer = new XmlSerializer(typeof (LandData));
        
        private Vector3 _AABBMax = new Vector3();
        private Vector3 _AABBMin = new Vector3();
        private int _area = 0;
        private uint _auctionID = 0; //Unemplemented. If set to 0, not being auctioned
        private UUID _authBuyerID = UUID.Zero; //Unemplemented. Authorized Buyer's UUID
        private ParcelCategory _category = ParcelCategory.None; //Unemplemented. Parcel's chosen category
        private int _claimDate = 0;
        private int _claimPrice = 0; //Unemplemented
        private UUID _globalID = UUID.Zero;
        private UUID _groupID = UUID.Zero;
        private int _groupPrims = 0;
        private bool _isGroupOwned = false;
        private byte[] _bitmap = new byte[512];
        private string _description = String.Empty;


        private uint _flags = (uint) ParcelFlags.AllowFly | (uint) ParcelFlags.AllowLandmark |
                                (uint) ParcelFlags.AllowAPrimitiveEntry |
                                (uint) ParcelFlags.AllowDeedToGroup | (uint) ParcelFlags.AllowTerraform |
                                (uint) ParcelFlags.CreateObjects | (uint) ParcelFlags.AllowOtherScripts |
                                (uint) ParcelFlags.SoundLocal;

        private byte _landingType = 0;
        private string _name = "Your Parcel";
        private ParcelStatus _status = ParcelStatus.Leased;
        private int _localID = 0;
        private byte _mediaAutoScale = 0;
        private UUID _mediaID = UUID.Zero;

        private string _mediaURL = String.Empty;
        private string _musicURL = String.Empty;
        private int _otherPrims = 0;
        private UUID _ownerID = UUID.Zero;
        private int _ownerPrims = 0;
        private List<ParcelManager.ParcelAccessEntry> _parcelAccessList = new List<ParcelManager.ParcelAccessEntry>();
        private float _passHours = 0;
        private int _passPrice = 0;
        private int _salePrice = 0; //Unemeplemented. Parcels price.
        private int _selectedPrims = 0;
        private int _simwideArea = 0;
        private int _simwidePrims = 0;
        private UUID _snapshotID = UUID.Zero;
        private Vector3 _userLocation = new Vector3();
        private Vector3 _userLookAt = new Vector3();
        private int _dwell = 0;
        private int _otherCleanTime = 0;

        /// <summary>
        /// Upper corner of the AABB for the parcel
        /// </summary>
        [XmlIgnore]
        public Vector3 AABBMax {
            get {
                return _AABBMax;
            }
            set {
                _AABBMax = value;
            }
        }
        /// <summary>
        /// Lower corner of the AABB for the parcel
        /// </summary>
        [XmlIgnore]
        public Vector3 AABBMin {
            get {
                return _AABBMin;
            }
            set {
                _AABBMin = value;
            }
        }

        /// <summary>
        /// Area in meters^2 the parcel contains
        /// </summary>
        public int Area {
            get {
                return _area;
            }
            set {
                _area = value;
            }
        }

        /// <summary>
        /// ID of auction (3rd Party Integration) when parcel is being auctioned
        /// </summary>
        public uint AuctionID {
            get {
                return _auctionID;
            }
            set {
                _auctionID = value;
            }
        }

        /// <summary>
        /// UUID of authorized buyer of parcel.  This is UUID.Zero if anyone can buy it.
        /// </summary>
        public UUID AuthBuyerID {
            get {
                return _authBuyerID;
            }
            set {
                _authBuyerID = value;
            }
        }

        /// <summary>
        /// Category of parcel.  Used for classifying the parcel in classified listings
        /// </summary>
        public ParcelCategory Category {
            get {
                return _category;
            }
            set {
                _category = value;
            }
        }

        /// <summary>
        /// Date that the current owner purchased or claimed the parcel
        /// </summary>
        public int ClaimDate {
            get {
                return _claimDate;
            }
            set {
                _claimDate = value;
            }
        }

        /// <summary>
        /// The last price that the parcel was sold at
        /// </summary>
        public int ClaimPrice {
            get {
                return _claimPrice;
            }
            set {
                _claimPrice = value;
            }
        }

        /// <summary>
        /// Global ID for the parcel.  (3rd Party Integration)
        /// </summary>
        public UUID GlobalID {
            get {
                return _globalID;
            }
            set {
                _globalID = value;
            }
        }

        /// <summary>
        /// Unique ID of the Group that owns
        /// </summary>
        public UUID GroupID {
            get {
                return _groupID;
            }
            set {
                _groupID = value;
            }
        }

        /// <summary>
        /// Number of SceneObjectPart that are owned by a Group
        /// </summary>
        [XmlIgnore]
        public int GroupPrims {
            get {
                return _groupPrims;
            }
            set {
                _groupPrims = value;
            }
        }

        /// <summary>
        /// Returns true if the Land Parcel is owned by a group
        /// </summary>
        public bool IsGroupOwned {
            get {
                return _isGroupOwned;
            }
            set {
                _isGroupOwned = value;
            }
        }

        /// <summary>
        /// jp2 data for the image representative of the parcel in the parcel dialog
        /// </summary>
        public byte[] Bitmap {
            get {
                return _bitmap;
            }
            set {
                _bitmap = value;
            }
        }

        /// <summary>
        /// Parcel Description
        /// </summary>
        public string Description {
            get {
                return _description;
            }
            set {
                _description = value;
            }
        }

        /// <summary>
        /// Parcel settings.  Access flags, Fly, NoPush, Voice, Scripts allowed, etc.  ParcelFlags
        /// </summary>
        public uint Flags {
            get {
                return _flags;
            }
            set {
                _flags = value;
            }
        }

        /// <summary>
        /// Determines if people are able to teleport where they please on the parcel or if they 
        /// get constrainted to a specific point on teleport within the parcel
        /// </summary>
        public byte LandingType {
            get {
                return _landingType;
            }
            set {
                _landingType = value;
            }
        }

        /// <summary>
        /// Parcel Name
        /// </summary>
        public string Name {
            get {
                return _name;
            }
            set {
                _name = value;
            }
        }

        /// <summary>
        /// Status of Parcel, Leased, Abandoned, For Sale
        /// </summary>
        public ParcelStatus Status {
            get {
                return _status;
            }
            set {
                _status = value;
            }
        }

        /// <summary>
        /// Internal ID of the parcel.  Sometimes the client will try to use this value
        /// </summary>
        public int LocalID {
            get {
                return _localID;
            }
            set {
                _localID = value;
            }
        }

        /// <summary>
        /// Determines if we scale the media based on the surface it's on
        /// </summary>
        public byte MediaAutoScale {
            get {
                return _mediaAutoScale;
            }
            set {
                _mediaAutoScale = value;
            }
        }

        /// <summary>
        /// Texture Guid to replace with the output of the media stream
        /// </summary>
        public UUID MediaID {
            get {
                return _mediaID;
            }
            set {
                _mediaID = value;
            }
        }

        /// <summary>
        /// URL to the media file to display
        /// </summary>
        public string MediaURL {
            get {
                return _mediaURL;
            }
            set {
                _mediaURL = value;
            }
        }

        private int[] _mediaSize = new int[2];
        public int[] MediaSize
        {
            get
            {
                return _mediaSize;
            }
            set
            {
                _mediaSize = value;
            }
        }

        private string _mediaType = "";
        public string MediaType
        {
            get
            {
                return _mediaType;
            }
            set
            {
                _mediaType = value;
            }
        }

        /// <summary>
        /// URL to the shoutcast music stream to play on the parcel
        /// </summary>
        public string MusicURL {
            get {
                return _musicURL;
            }
            set {
                _musicURL = value;
            }
        }

        /// <summary>
        /// Number of SceneObjectPart that are owned by users who do not own the parcel
        /// and don't have the 'group.  These are elegable for AutoReturn collection
        /// </summary>
        [XmlIgnore]
        public int OtherPrims {
            get {
                return _otherPrims;
            }
            set {
                _otherPrims = value;
            }
        }

        /// <summary>
        /// Owner Avatar or Group of the parcel.  Naturally, all land masses must be
        /// owned by someone
        /// </summary>
        public UUID OwnerID {
            get {
                return _ownerID;
            }
            set {
                _ownerID = value;
            }
        }

        /// <summary>
        /// Number of SceneObjectPart that are owned by the owner of the parcel
        /// </summary>
        [XmlIgnore]
        public int OwnerPrims {
            get {
                return _ownerPrims;
            }
            set {
                _ownerPrims = value;
            }
        }

        /// <summary>
        /// List of access data for the parcel.  User data, some bitflags, and a time
        /// </summary>
        public List<ParcelManager.ParcelAccessEntry> ParcelAccessList {
            get {
                return _parcelAccessList;
            }
            set {
                _parcelAccessList = value;
            }
        }

        /// <summary>
        /// How long in hours a Pass to the parcel is given
        /// </summary>
        public float PassHours {
            get {
                return _passHours;
            }
            set {
                _passHours = value;
            }
        }

        /// <summary>
        /// Price to purchase a Pass to a restricted parcel
        /// </summary>
        public int PassPrice {
            get {
                return _passPrice;
            }
            set {
                _passPrice = value;
            }
        }

        /// <summary>
        /// When the parcel is being sold, this is the price to purchase the parcel
        /// </summary>
        public int SalePrice {
            get {
                return _salePrice;
            }
            set {
                _salePrice = value;
            }
        }

        /// <summary>
        /// Number of SceneObjectPart that are currently selected by avatar
        /// </summary>
        [XmlIgnore]
        public int SelectedPrims {
            get {
                return _selectedPrims;
            }
            set {
                _selectedPrims = value;
            }
        }

        /// <summary>
        /// Number of meters^2 in the Simulator
        /// </summary>
        [XmlIgnore]
        public int SimwideArea {
            get {
                return _simwideArea;
            }
            set {
                _simwideArea = value;
            }
        }

        /// <summary>
        /// Number of SceneObjectPart in the Simulator
        /// </summary>
        [XmlIgnore]
        public int SimwidePrims {
            get {
                return _simwidePrims;
            }
            set {
                _simwidePrims = value;
            }
        }

        /// <summary>
        /// ID of the snapshot used in the client parcel dialog of the parcel
        /// </summary>
        public UUID SnapshotID {
            get {
                return _snapshotID;
            }
            set {
                _snapshotID = value;
            }
        }

        /// <summary>
        /// When teleporting is restricted to a certain point, this is the location 
        /// that the user will be redirected to
        /// </summary>
        public Vector3 UserLocation {
            get {
                return _userLocation;
            }
            set {
                _userLocation = value;
            }
        }

        /// <summary>
        /// When teleporting is restricted to a certain point, this is the rotation 
        /// that the user will be positioned
        /// </summary>
        public Vector3 UserLookAt {
            get {
                return _userLookAt;
            }
            set {
                _userLookAt = value;
            }
        }

        /// <summary>
        /// Deprecated idea.  Number of visitors ~= free money
        /// </summary>
        public int Dwell {
            get {
                return _dwell;
            }
            set {
                _dwell = value;
            }
        }

        /// <summary>
        /// Number of minutes to return SceneObjectGroup that are owned by someone who doesn't own 
        /// the parcel and isn't set to the same 'group' as the parcel.
        /// </summary>
        public int OtherCleanTime {
            get {
                return _otherCleanTime;
            }
            set {
                _otherCleanTime = value;
            }
        }


        public LandData()
        {
            _globalID = UUID.Random();
        }

        /// <summary>
        /// Make a new copy of the land data
        /// </summary>
        /// <returns></returns>
        public LandData Copy()
        {
            LandData landData = new LandData();

            landData._AABBMax = _AABBMax;
            landData._AABBMin = _AABBMin;
            landData._area = _area;
            landData._auctionID = _auctionID;
            landData._authBuyerID = _authBuyerID;
            landData._category = _category;
            landData._claimDate = _claimDate;
            landData._claimPrice = _claimPrice;
            landData._globalID = _globalID;
            landData._groupID = _groupID;
            landData._groupPrims = _groupPrims;
            landData._otherPrims = _otherPrims;
            landData._ownerPrims = _ownerPrims;
            landData._selectedPrims = _selectedPrims;
            landData._isGroupOwned = _isGroupOwned;
            landData._localID = _localID;
            landData._landingType = _landingType;
            landData._mediaAutoScale = _mediaAutoScale;
            landData._mediaID = _mediaID;
            landData._mediaURL = _mediaURL;
            landData._musicURL = _musicURL;
            landData._ownerID = _ownerID;
            landData._bitmap = (byte[]) _bitmap.Clone();
            landData._description = _description;
            landData._flags = _flags;
            landData._name = _name;
            landData._status = _status;
            landData._passHours = _passHours;
            landData._passPrice = _passPrice;
            landData._salePrice = _salePrice;
            landData._snapshotID = _snapshotID;
            landData._userLocation = _userLocation;
            landData._userLookAt = _userLookAt;
            landData._otherCleanTime = _otherCleanTime;
            landData._dwell = _dwell;

            landData._parcelAccessList.Clear();
            foreach (ParcelManager.ParcelAccessEntry entry in _parcelAccessList)
            {
                ParcelManager.ParcelAccessEntry newEntry = new ParcelManager.ParcelAccessEntry();
                newEntry.AgentID = entry.AgentID;
                newEntry.Flags = entry.Flags;
                newEntry.Time = entry.Time;

                landData._parcelAccessList.Add(newEntry);
            }

            return landData;
        }

        public void ToXml(XmlWriter xmlWriter)
        {
            serializer.Serialize(xmlWriter, this);
        }

        /// <summary>
        /// Restore a LandData object from the serialized xml representation.
        /// </summary>
        /// <param name="xmlReader"></param>
        /// <returns></returns>
        public static LandData FromXml(XmlReader xmlReader)
        {
            LandData land = (LandData)serializer.Deserialize(xmlReader);

            return land;
        }
    }
}
