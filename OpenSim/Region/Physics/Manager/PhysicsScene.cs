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
using System.Reflection;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenMetaverse;

namespace OpenSim.Region.Physics.Manager
{
    public delegate void physicsCrash();

    public abstract class PhysicsScene
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // The only thing that should register for this event is the SceneGraph
        // Anything else could cause problems.

        public event physicsCrash OnPhysicsCrash;

        public static PhysicsScene Null
        {
            get { return new NullPhysicsScene(); }
        }

        public virtual void TriggerPhysicsBasedRestart()
        {
            physicsCrash handler = OnPhysicsCrash;
            if (handler != null)
            {
                OnPhysicsCrash();
            }
        }


        public abstract void Initialise(IMesher meshmerizer, IConfigSource config);

        public abstract PhysicsActor AddAvatar(string avName, PhysicsVector position, PhysicsVector size, bool isFlying);

        public abstract void RemoveAvatar(PhysicsActor actor);

        public abstract void RemovePrim(PhysicsActor prim);

        public abstract PhysicsActor AddPrimShape(string primName, PrimitiveBaseShape pbs, PhysicsVector position,
                                                  PhysicsVector size, Quaternion rotation); //To be removed
        public abstract PhysicsActor AddPrimShape(string primName, PrimitiveBaseShape pbs, PhysicsVector position,
                                                  PhysicsVector size, Quaternion rotation, bool isPhysical);

        public virtual bool SupportsNINJAJoints
        {
            get { return false; }
        }

        public virtual PhysicsJoint RequestJointCreation(string objectNameInScene, PhysicsJointType jointType, PhysicsVector position,
                                            Quaternion rotation, string parms, List<string> bodyNames, string trackedBodyName, Quaternion localRotation)
        { return null; }

        public virtual void RequestJointDeletion(string objectNameInScene)
        { return; }

        public virtual void RemoveAllJointsConnectedToActorThreadLocked(PhysicsActor actor)
        { return; }

        public virtual void DumpJointInfo()
        { return; }

        public event JointMoved OnJointMoved;

        protected virtual void DoJointMoved(PhysicsJoint joint)
        {
            // We need this to allow subclasses (but not other classes) to invoke the event; C# does
            // not allow subclasses to invoke the parent class event.
            if (OnJointMoved != null)
            {
                OnJointMoved(joint);
            }
        }

        public event JointDeactivated OnJointDeactivated;

        protected virtual void DoJointDeactivated(PhysicsJoint joint)
        {
            // We need this to allow subclasses (but not other classes) to invoke the event; C# does
            // not allow subclasses to invoke the parent class event.
            if (OnJointDeactivated != null)
            {
                OnJointDeactivated(joint);
            }
        }

        public event JointErrorMessage OnJointErrorMessage;

        protected virtual void DoJointErrorMessage(PhysicsJoint joint, string message)
        {
            // We need this to allow subclasses (but not other classes) to invoke the event; C# does
            // not allow subclasses to invoke the parent class event.
            if (OnJointErrorMessage != null)
            {
                OnJointErrorMessage(joint, message);
            }
        }

        public virtual PhysicsVector GetJointAnchor(PhysicsJoint joint)
        { return null; }

        public virtual PhysicsVector GetJointAxis(PhysicsJoint joint)
        { return null; }


        public abstract void AddPhysicsActorTaint(PhysicsActor prim);

        public abstract float Simulate(float timeStep);

        public abstract void GetResults();

        public abstract void SetTerrain(float[] heightMap);

        public abstract void SetWaterLevel(float baseheight);

        public abstract void DeleteTerrain();

        public abstract void Dispose();

        public abstract Dictionary<uint, float> GetTopColliders();

        public abstract bool IsThreaded { get; }

        private class NullPhysicsScene : PhysicsScene
        {
            private static int m_workIndicator;


            public override void Initialise(IMesher meshmerizer, IConfigSource config)
            {
                // Does nothing right now
            }

            public override PhysicsActor AddAvatar(string avName, PhysicsVector position, PhysicsVector size, bool isFlying)
            {
                m_log.InfoFormat("[PHYSICS]: NullPhysicsScene : AddAvatar({0})", position);
                return PhysicsActor.Null;
            }

            public override void RemoveAvatar(PhysicsActor actor)
            {
            }

            public override void RemovePrim(PhysicsActor prim)
            {
            }
            public override void SetWaterLevel(float baseheight)
            {

            }

/*
            public override PhysicsActor AddPrim(PhysicsVector position, PhysicsVector size, Quaternion rotation)
            {
                m_log.InfoFormat("NullPhysicsScene : AddPrim({0},{1})", position, size);
                return PhysicsActor.Null;
            }
*/

            public override PhysicsActor AddPrimShape(string primName, PrimitiveBaseShape pbs, PhysicsVector position,
                                                      PhysicsVector size, Quaternion rotation) //To be removed
            {
                return AddPrimShape(primName, pbs, position, size, rotation, false);
            }

            public override PhysicsActor AddPrimShape(string primName, PrimitiveBaseShape pbs, PhysicsVector position,
                                                      PhysicsVector size, Quaternion rotation, bool isPhysical)
            {
                m_log.InfoFormat("[PHYSICS]: NullPhysicsScene : AddPrim({0},{1})", position, size);
                return PhysicsActor.Null;
            }

            public override void AddPhysicsActorTaint(PhysicsActor prim)
            {
            }

            public override float Simulate(float timeStep)
            {
                m_workIndicator = (m_workIndicator + 1) % 10;

                return 0f;
            }

            public override void GetResults()
            {
                m_log.Info("[PHYSICS]: NullPhysicsScene : GetResults()");
            }

            public override void SetTerrain(float[] heightMap)
            {
                m_log.InfoFormat("[PHYSICS]: NullPhysicsScene : SetTerrain({0} items)", heightMap.Length);
            }

            public override void DeleteTerrain()
            {
            }

            public override bool IsThreaded
            {
                get { return false; }
            }

            public override void Dispose()
            {
            }

            public override Dictionary<uint,float> GetTopColliders()
            {
                Dictionary<uint, float> returncolliders = new Dictionary<uint, float>();
                return returncolliders;
            }
        }
    }
    public delegate void JointMoved(PhysicsJoint joint);
    public delegate void JointDeactivated(PhysicsJoint joint);
    public delegate void JointErrorMessage(PhysicsJoint joint, string message); // this refers to an "error message due to a problem", not "amount of joint constraint violation"
}
