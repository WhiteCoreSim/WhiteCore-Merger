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
//#define SPAM

using System;
using System.Collections.Generic;
using OpenSim.Framework;
using OpenSim.Region.Physics.Manager;
using OpenMetaverse;
using OpenMetaverse.Imaging;
using System.Drawing;
using System.Drawing.Imaging;
using PrimMesher;
using log4net;
using System.Reflection;

namespace OpenSim.Region.Physics.Meshing
{
    public class MeshmerizerPlugin : IMeshingPlugin
    {
        public MeshmerizerPlugin()
        {
        }

        public string GetName()
        {
            return "Meshmerizer";
        }

        public IMesher GetMesher()
        {
            return new Meshmerizer();
        }
    }

    public class Meshmerizer : IMesher
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        //private static readonly log4net.ILog m_log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // Setting baseDir to a path will enable the dumping of raw files
        // raw files can be imported by blender so a visual inspection of the results can be done
#if SPAM
        const string baseDir = "rawFiles";
#else
        private const string baseDir = null; //"rawFiles";
#endif

        private float minSizeForComplexMesh = 0.2f; // prims with all dimensions smaller than this will have a bounding box mesh


        /// <summary>
        /// creates a simple box mesh of the specified size. This mesh is of very low vertex count and may
        /// be useful as a backup proxy when level of detail is not needed or when more complex meshes fail
        /// for some reason
        /// </summary>
        /// <param name="minX"></param>
        /// <param name="maxX"></param>
        /// <param name="minY"></param>
        /// <param name="maxY"></param>
        /// <param name="minZ"></param>
        /// <param name="maxZ"></param>
        /// <returns></returns>
        private static Mesh CreateSimpleBoxMesh(float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
        {
            Mesh box = new Mesh();

            // bottom

            box.Add(new Vertex(minX, maxY, minZ));
            box.Add(new Vertex(maxX, maxY, minZ));
            box.Add(new Vertex(maxX, minY, minZ));
            box.Add(new Vertex(minX, minY, minZ));

            box.Add(new Triangle(box.vertices[0], box.vertices[1], box.vertices[2]));
            box.Add(new Triangle(box.vertices[0], box.vertices[2], box.vertices[3]));

            // top

            box.Add(new Vertex(maxX, maxY, maxZ));
            box.Add(new Vertex(minX, maxY, maxZ));
            box.Add(new Vertex(minX, minY, maxZ));
            box.Add(new Vertex(maxX, minY, maxZ));

            box.Add(new Triangle(box.vertices[4], box.vertices[5], box.vertices[6]));
            box.Add(new Triangle(box.vertices[4], box.vertices[6], box.vertices[7]));

            // sides

            box.Add(new Triangle(box.vertices[5], box.vertices[0], box.vertices[3]));
            box.Add(new Triangle(box.vertices[5], box.vertices[3], box.vertices[6]));

            box.Add(new Triangle(box.vertices[1], box.vertices[0], box.vertices[5]));
            box.Add(new Triangle(box.vertices[1], box.vertices[5], box.vertices[4]));

            box.Add(new Triangle(box.vertices[7], box.vertices[1], box.vertices[4]));
            box.Add(new Triangle(box.vertices[7], box.vertices[2], box.vertices[1]));

            box.Add(new Triangle(box.vertices[3], box.vertices[2], box.vertices[7]));
            box.Add(new Triangle(box.vertices[3], box.vertices[7], box.vertices[6]));

            return box;
        }


        /// <summary>
        /// Creates a simple bounding box mesh for a complex input mesh
        /// </summary>
        /// <param name="meshIn"></param>
        /// <returns></returns>
        private static Mesh CreateBoundingBoxMesh(Mesh meshIn)
        {
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;

            foreach (Vertex v in meshIn.vertices)
            {
                if (v != null)
                {
                    if (v.X < minX) minX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.Z < minZ) minZ = v.Z;

                    if (v.X > maxX) maxX = v.X;
                    if (v.Y > maxY) maxY = v.Y;
                    if (v.Z > maxZ) maxZ = v.Z;
                }
            }

            return CreateSimpleBoxMesh(minX, maxX, minY, maxY, minZ, maxZ);
        }

        private void ReportPrimError(string message, string primName, PrimMesh primMesh)
        {
            m_log.Error(message);
            m_log.Error("\nPrim Name: " + primName);
            m_log.Error("****** PrimMesh Parameters ******\n" + primMesh.ParamsToDisplayString());

        }

        public Mesh CreateMeshFromPrimMesher(string primName, PrimitiveBaseShape primShape, PhysicsVector size, float lod)
        {
            Mesh mesh = new Mesh();
            PrimMesh primMesh;
            PrimMesher.SculptMesh sculptMesh;

            List<Coord> coords;
            List<Face> faces;

            Image idata = null;

            if (primShape.SculptEntry)
            {
                if (primShape.SculptData.Length == 0)
                    return null;

                try
                {
                    ManagedImage managedImage;  // we never use this
                    OpenJPEG.DecodeToImage(primShape.SculptData, out managedImage, out idata);
                    
                }
                catch (DllNotFoundException)
                {
                    m_log.Error("[PHYSICS]: OpenJpeg is not installed correctly on this system. Physics Proxy generation failed.  Often times this is because of an old version of GLIBC.  You must have version 2.4 or above!");
                    return null;
                }
                catch (IndexOutOfRangeException)
                {
                    m_log.Error("[PHYSICS]: OpenJpeg was unable to decode this.   Physics Proxy generation failed");
                    return null;
                }
                catch (Exception)
                {
                    m_log.Error("[PHYSICS]: Unable to generate a Sculpty physics proxy.  Sculpty texture decode failed!");
                    return null;
                }

                PrimMesher.SculptMesh.SculptType sculptType;
                switch ((OpenMetaverse.SculptType)primShape.SculptType)
                {
                    case OpenMetaverse.SculptType.Cylinder:
                        sculptType = PrimMesher.SculptMesh.SculptType.cylinder;
                        break;
                    case OpenMetaverse.SculptType.Plane:
                        sculptType = PrimMesher.SculptMesh.SculptType.plane;
                        break;
                    case OpenMetaverse.SculptType.Torus:
                        sculptType = PrimMesher.SculptMesh.SculptType.torus;
                        break;
                    case OpenMetaverse.SculptType.Sphere:
                        sculptType = PrimMesher.SculptMesh.SculptType.sphere;
                        break;
                    default:
                        sculptType = PrimMesher.SculptMesh.SculptType.plane;
                        break;
                }

                bool mirror = ((primShape.SculptType & 128) != 0);
                bool invert = ((primShape.SculptType & 64) != 0);

                sculptMesh = new PrimMesher.SculptMesh((Bitmap)idata, sculptType, (int)lod, false, mirror, invert);

                idata.Dispose();

                sculptMesh.DumpRaw(baseDir, primName, "primMesh");

                sculptMesh.Scale(size.X, size.Y, size.Z);

                coords = sculptMesh.coords;
                faces = sculptMesh.faces;
            }

            else
            {
                float pathShearX = primShape.PathShearX < 128 ? (float)primShape.PathShearX * 0.01f : (float)(primShape.PathShearX - 256) * 0.01f;
                float pathShearY = primShape.PathShearY < 128 ? (float)primShape.PathShearY * 0.01f : (float)(primShape.PathShearY - 256) * 0.01f;
                float pathBegin = (float)primShape.PathBegin * 2.0e-5f;
                float pathEnd = 1.0f - (float)primShape.PathEnd * 2.0e-5f;
                float pathScaleX = (float)(primShape.PathScaleX - 100) * 0.01f;
                float pathScaleY = (float)(primShape.PathScaleY - 100) * 0.01f;

                float profileBegin = (float)primShape.ProfileBegin * 2.0e-5f;
                float profileEnd = 1.0f - (float)primShape.ProfileEnd * 2.0e-5f;
                float profileHollow = (float)primShape.ProfileHollow * 2.0e-5f;
                if (profileHollow > 0.95f)
                    profileHollow = 0.95f;

                int sides = 4;
                if ((primShape.ProfileCurve & 0x07) == (byte)ProfileShape.EquilateralTriangle)
                    sides = 3;
                else if ((primShape.ProfileCurve & 0x07) == (byte)ProfileShape.Circle)
                    sides = 24;
                else if ((primShape.ProfileCurve & 0x07) == (byte)ProfileShape.HalfCircle)
                { // half circle, prim is a sphere
                    sides = 24;

                    profileBegin = 0.5f * profileBegin + 0.5f;
                    profileEnd = 0.5f * profileEnd + 0.5f;

                }

                int hollowSides = sides;
                if (primShape.HollowShape == HollowShape.Circle)
                    hollowSides = 24;
                else if (primShape.HollowShape == HollowShape.Square)
                    hollowSides = 4;
                else if (primShape.HollowShape == HollowShape.Triangle)
                    hollowSides = 3;

                primMesh = new PrimMesh(sides, profileBegin, profileEnd, profileHollow, hollowSides);

                if (primMesh.errorMessage != null)
                    if (primMesh.errorMessage.Length > 0)
                        m_log.Error("[ERROR] " + primMesh.errorMessage);

                primMesh.topShearX = pathShearX;
                primMesh.topShearY = pathShearY;
                primMesh.pathCutBegin = pathBegin;
                primMesh.pathCutEnd = pathEnd;

                if (primShape.PathCurve == (byte)Extrusion.Straight)
                {
                    primMesh.twistBegin = primShape.PathTwistBegin * 18 / 10;
                    primMesh.twistEnd = primShape.PathTwist * 18 / 10;
                    primMesh.taperX = pathScaleX;
                    primMesh.taperY = pathScaleY;

                    if (profileBegin < 0.0f || profileBegin >= profileEnd || profileEnd > 1.0f)
                    {
                        ReportPrimError("*** CORRUPT PRIM!! ***", primName, primMesh);
                        if (profileBegin < 0.0f) profileBegin = 0.0f;
                        if (profileEnd > 1.0f) profileEnd = 1.0f;
                    }
#if SPAM
                m_log.Debug("****** PrimMesh Parameters (Linear) ******\n" + primMesh.ParamsToDisplayString());
#endif
                    try
                    {
                        primMesh.ExtrudeLinear();
                    }
                    catch (Exception ex)
                    {
                        ReportPrimError("Extrusion failure: exception: " + ex.ToString(), primName, primMesh);
                        return null;
                    }
                }
                else
                {
                    primMesh.holeSizeX = (200 - primShape.PathScaleX) * 0.01f;
                    primMesh.holeSizeY = (200 - primShape.PathScaleY) * 0.01f;
                    primMesh.radius = 0.01f * primShape.PathRadiusOffset;
                    primMesh.revolutions = 1.0f + 0.015f * primShape.PathRevolutions;
                    primMesh.skew = 0.01f * primShape.PathSkew;
                    primMesh.twistBegin = primShape.PathTwistBegin * 36 / 10;
                    primMesh.twistEnd = primShape.PathTwist * 36 / 10;
                    primMesh.taperX = primShape.PathTaperX * 0.01f;
                    primMesh.taperY = primShape.PathTaperY * 0.01f;

                    if (profileBegin < 0.0f || profileBegin >= profileEnd || profileEnd > 1.0f)
                    {
                        ReportPrimError("*** CORRUPT PRIM!! ***", primName, primMesh);
                        if (profileBegin < 0.0f) profileBegin = 0.0f;
                        if (profileEnd > 1.0f) profileEnd = 1.0f;
                    }
#if SPAM
                m_log.Debug("****** PrimMesh Parameters (Circular) ******\n" + primMesh.ParamsToDisplayString());
#endif
                    try
                    {
                        primMesh.ExtrudeCircular();
                    }
                    catch (Exception ex)
                    {
                        ReportPrimError("Extrusion failure: exception: " + ex.ToString(), primName, primMesh);
                        return null;
                    }
                }

                primMesh.DumpRaw(baseDir, primName, "primMesh");

                primMesh.Scale(size.X, size.Y, size.Z);

                coords = primMesh.coords;
                faces = primMesh.faces;

            }


            int numCoords = coords.Count;
            int numFaces = faces.Count;

            for (int i = 0; i < numCoords; i++)
            {
                Coord c = coords[i];
                mesh.vertices.Add(new Vertex(c.X, c.Y, c.Z));
            }

            List<Vertex> vertices = mesh.vertices;
            for (int i = 0; i < numFaces; i++)
            {
                Face f = faces[i];
                mesh.triangles.Add(new Triangle(vertices[f.v1], vertices[f.v2], vertices[f.v3]));
            }

            return mesh;
        }

        public IMesh CreateMesh(String primName, PrimitiveBaseShape primShape, PhysicsVector size, float lod)
        {
            return CreateMesh(primName, primShape, size, lod, false);
        }

        public IMesh CreateMesh(String primName, PrimitiveBaseShape primShape, PhysicsVector size, float lod, bool isPhysical)
        {
            Mesh mesh = null;

            if (size.X < 0.01f) size.X = 0.01f;
            if (size.Y < 0.01f) size.Y = 0.01f;
            if (size.Z < 0.01f) size.Z = 0.01f;

            mesh = CreateMeshFromPrimMesher(primName, primShape, size, lod);

            if (mesh != null)
            {
                if ((!isPhysical) && size.X < minSizeForComplexMesh && size.Y < minSizeForComplexMesh && size.Z < minSizeForComplexMesh)
                {
#if SPAM
                m_log.Debug("Meshmerizer: prim " + primName + " has a size of " + size.ToString() + " which is below threshold of " + 

minSizeForComplexMesh.ToString() + " - creating simple bounding box" );
#endif
                    mesh = CreateBoundingBoxMesh(mesh);
                    mesh.DumpRaw(baseDir, primName, "Z extruded");
                }

                // trim the vertex and triangle lists to free up memory
                mesh.vertices.TrimExcess();
                mesh.triangles.TrimExcess();
            }

            return mesh;
        }

    }
}
