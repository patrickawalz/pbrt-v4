// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// cameras.h / cameras.cpp

using System;
using System.Collections.Generic;

namespace pbrt
{

    // CameraTransform Definition
    public unsafe class CameraTransform
    {
        // CameraTransform Public Methods
        public CameraTransform() { }

        // CameraTransform Method Definitions
        public CameraTransform(AnimatedTransform worldFromCamera)
        {
            switch (pbrt.Options.renderingSpace)
            {
                case RenderingCoordinateSystem.Camera:
                    {
                        // Compute _worldFromRender_ for camera-space rendering
                        Float tMid = (worldFromCamera.startTime + worldFromCamera.endTime) / 2;
                        worldFromRender = worldFromCamera.Interpolate(tMid);
                        break;
                    }
                case RenderingCoordinateSystem.CameraWorld:
                    {
                        // Compute _worldFromRender_ for camera-world space rendering
                        Float tMid = (worldFromCamera.startTime + worldFromCamera.endTime) / 2;
                        Point3f pCamera = worldFromCamera.Apply(new Point3f(0, 0, 0), tMid);
                        worldFromRender = pbrt.Translate(new Vector3f(pCamera.x, pCamera.y, pCamera.z));
                        break;
                    }
                case RenderingCoordinateSystem.World:
                    {
                        // Compute _worldFromRender_ for world-space rendering
                        worldFromRender = new Transform();
                        break;
                    }
                default:
                    pbrt.LOG_FATAL("Unhandled rendering coordinate space");
                    break;
            }
            pbrt.LOG_VERBOSE("World-space position: %s", worldFromRender.Apply(new Point3f(0, 0, 0)));
            // Compute _renderFromCamera_ transformation
            Transform renderFromWorld = pbrt.Inverse(worldFromRender);
            Transform[] rfc = { renderFromWorld * worldFromCamera.startTransform,
                                renderFromWorld * worldFromCamera.endTransform };
            renderFromCamera = new AnimatedTransform(rfc[0], worldFromCamera.startTime, rfc[1], worldFromCamera.endTime);
        }

        public Point3f RenderFromCamera(Point3f p, Float time) { return renderFromCamera.Apply(p, time); }
        public Point3f CameraFromRender(Point3f p, Float time) { return renderFromCamera.ApplyInverse(p, time); }
        public Point3f RenderFromWorld(Point3f p) { return worldFromRender.ApplyInverse(p); }

        public Transform RenderFromWorld() { return pbrt.Inverse(worldFromRender); }
        public Transform CameraFromRender(Float time) { return pbrt.Inverse(renderFromCamera.Interpolate(time)); }
        public Transform CameraFromWorld(Float time)
        {
            return pbrt.Inverse(worldFromRender * renderFromCamera.Interpolate(time));
        }

        public bool CameraFromRenderHasScale() { return renderFromCamera.HasScale(); }

        public Vector3f RenderFromCamera(Vector3f v, Float time) { return renderFromCamera.Apply(v, time); }

        public Normal3f RenderFromCamera(Normal3f n, Float time) { return renderFromCamera.Apply(n, time); }

        public Ray RenderFromCamera(Ray r) { return renderFromCamera.Apply(r); }

        public RayDifferential RenderFromCamera(RayDifferential r) { return renderFromCamera.Apply(r); }

        public Vector3f CameraFromRender(Vector3f v, Float time) { return renderFromCamera.ApplyInverse(v, time); }

        public Normal3f CameraFromRender(Normal3f v, Float time) { return renderFromCamera.ApplyInverse(v, time); }

        public AnimatedTransform RenderFromCamera() { return renderFromCamera; }

        public Transform WorldFromRender() { return worldFromRender; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ CameraTransform renderFromCamera: %s worldFromRender: %s ]",
                                        renderFromCamera, worldFromRender);
        }

        // CameraTransform Private Members
        AnimatedTransform renderFromCamera = new AnimatedTransform();
        Transform worldFromRender = new Transform();
    }

    // CameraWiSample Definition
    public unsafe struct CameraWiSample
    {
        // CameraWiSample Public Methods
        public CameraWiSample(SampledSpectrum Wi, Vector3f wi, Float pdf, Point2f pRaster, Interaction pRef,
                              Interaction pLens)
        {
            this.Wi = Wi; this.wi = wi; this.pdf = pdf; this.pRaster = pRaster; this.pRef = pRef; this.pLens = pLens;
        }

        public SampledSpectrum Wi;
        public Vector3f wi;
        public Float pdf;
        public Point2f pRaster;
        public Interaction pRef, pLens;
    }

    // CameraRay Definition
    public unsafe struct CameraRay
    {
        public CameraRay(Ray ray) { this.ray = ray; this.weight = new SampledSpectrum(1); }
        public CameraRay(Ray ray, SampledSpectrum weight) { this.ray = ray; this.weight = weight; }

        public Ray ray;
        public SampledSpectrum weight;  // = SampledSpectrum(1)
    }

    // CameraRayDifferential Definition
    public unsafe struct CameraRayDifferential
    {
        public CameraRayDifferential(RayDifferential ray) { this.ray = ray; this.weight = new SampledSpectrum(1); }
        public CameraRayDifferential(RayDifferential ray, SampledSpectrum weight) { this.ray = ray; this.weight = weight; }

        public RayDifferential ray;
        public SampledSpectrum weight;  // = SampledSpectrum(1)
    }

    // CameraBaseParameters Definition
    public unsafe struct CameraBaseParameters
    {
        public CameraTransform cameraTransform;
        public Float shutterOpen, shutterClose;  // = 0, = 1
        public Film film;
        public Medium medium;

        // CameraBaseParameters Method Definitions
        public CameraBaseParameters(CameraTransform cameraTransform, Film film, Medium medium,
                                    ParameterDictionary parameters, FileLoc loc)
        {
            this.cameraTransform = cameraTransform;
            this.film = film;
            this.medium = medium;
            shutterOpen = parameters.GetOneFloat("shutteropen", 0.0f);
            shutterClose = parameters.GetOneFloat("shutterclose", 1.0f);
            if (shutterClose < shutterOpen)
            {
                pbrt.Warning(loc, pbrt.StringPrintf("Shutter close time %f < shutter open %f.  Swapping them.",
                                                          shutterClose, shutterOpen));
                pstd.swap(ref shutterClose, ref shutterOpen);
            }
        }
    }

    // CameraBase Definition
    public abstract unsafe class CameraBase : Camera
    {
        // CameraBase Public Methods
        public override Film GetFilm() { return film; }
        public override CameraTransform GetCameraTransform() { return cameraTransform; }

        public override Float SampleTime(Float u) { return pbrt.Lerp(u, shutterOpen, shutterClose); }

        // CameraBase Method Definitions
        public override void InitMetadata(ImageMetadata metadata)
        {
            metadata.cameraFromWorld = cameraTransform.CameraFromWorld(shutterOpen).GetMatrix();
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("cameraTransform: %s shutterOpen: %f shutterClose: %f film: %s " +
                                        "medium: %s minPosDifferentialX: %s minPosDifferentialY: %s " +
                                        "minDirDifferentialX: %s minDirDifferentialY: %s ",
                                        cameraTransform, shutterOpen, shutterClose, film,
                                        medium != null ? medium.ToString() : "(nullptr)",
                                        minPosDifferentialX, minPosDifferentialY, minDirDifferentialX,
                                        minDirDifferentialY);
        }

        public override void Approximate_dp_dxy(Point3f p, Normal3f n, Float time, int samplesPerPixel,
                                                Vector3f* dpdx, Vector3f* dpdy)
        {
            // Compute tangent plane equation for ray differential intersections
            Point3f pCamera = CameraFromRender(p, time);
            Transform DownZFromCamera =
                pbrt.RotateFromTo(pbrt.Normalize((Vector3f)pCamera), new Vector3f(0, 0, 1));
            Point3f pDownZ = DownZFromCamera.Apply(pCamera);
            Normal3f nDownZ = DownZFromCamera.Apply(CameraFromRender(n, time));
            Float d = nDownZ.z * pDownZ.z;

            // Find intersection points for approximated camera differential rays
            Ray xRay = new Ray(new Point3f(0, 0, 0) + minPosDifferentialX,
                               new Vector3f(0, 0, 1) + minDirDifferentialX);
            Float tx = -(pbrt.Dot(nDownZ, (Vector3f)xRay.o) - d) / pbrt.Dot(nDownZ, xRay.d);
            Ray yRay = new Ray(new Point3f(0, 0, 0) + minPosDifferentialY,
                               new Vector3f(0, 0, 1) + minDirDifferentialY);
            Float ty = -(pbrt.Dot(nDownZ, (Vector3f)yRay.o) - d) / pbrt.Dot(nDownZ, yRay.d);
            Point3f px = xRay.Apply(tx), py = yRay.Apply(ty);

            // Estimate $\dpdx$ and $\dpdy$ in tangent plane at intersection point
            Float sppScale = pbrt.Options.disablePixelJitter
                                 ? 1
                                 : std.max<Float>(.125f, 1 / std.sqrt((Float)samplesPerPixel));
            *dpdx = sppScale * RenderFromCamera(DownZFromCamera.ApplyInverse(px - pDownZ), time);
            *dpdy = sppScale * RenderFromCamera(DownZFromCamera.ApplyInverse(py - pDownZ), time);
        }

        // CameraBase Protected Members
        protected CameraTransform cameraTransform;
        protected Float shutterOpen, shutterClose;
        protected Film film;
        protected Medium medium;
        protected Vector3f minPosDifferentialX, minPosDifferentialY;
        protected Vector3f minDirDifferentialX, minDirDifferentialY;

        // CameraBase Protected Methods
        protected CameraBase() { }

        protected CameraBase(CameraBaseParameters p)
        {
            cameraTransform = p.cameraTransform;
            shutterOpen = p.shutterOpen;
            shutterClose = p.shutterClose;
            film = p.film;
            medium = p.medium;
            if (cameraTransform.CameraFromRenderHasScale())
                pbrt.Warning("Scaling detected in rendering space to camera space transformation!\n" +
                                "The system has numerous assumptions, implicit and explicit,\n" +
                                "that this transform will have no scale factors in it.\n" +
                                "Proceed at your own risk; your image may have errors or\n" +
                                "the system may crash as a result of this.");
        }

        protected static pstd.optional<CameraRayDifferential> GenerateRayDifferential(Camera camera, CameraSample sample,
                                                                                      ref SampledWavelengths lambda)
        {
            // Generate regular camera ray _cr_ for ray differential
            pstd.optional<CameraRay> cr = camera.GenerateRay(sample, ref lambda);
            if (!cr)
                return default;
            RayDifferential rd = new RayDifferential(cr.value().ray);

            // Find camera ray after shifting one pixel in the $x$ direction
            pstd.optional<CameraRay> rx = default;
            Float[] epss = { .05f, -.05f };
            foreach (Float eps in epss)
            {
                CameraSample sshift = sample;
                sshift.pFilm.x += eps;
                // Try to generate ray with _sshift_ and compute $x$ differential
                rx = camera.GenerateRay(sshift, ref lambda);
                if (rx)
                {
                    rd.rxOrigin = rd.o + (rx.value().ray.o - rd.o) / eps;
                    rd.rxDirection = rd.d + (rx.value().ray.d - rd.d) / eps;
                    break;
                }
            }

            // Find camera ray after shifting one pixel in the $y$ direction
            pstd.optional<CameraRay> ry = default;
            foreach (Float eps in epss)
            {
                CameraSample sshift = sample;
                sshift.pFilm.y += eps;
                ry = camera.GenerateRay(sshift, ref lambda);
                if (ry)
                {
                    rd.ryOrigin = rd.o + (ry.value().ray.o - rd.o) / eps;
                    rd.ryDirection = rd.d + (ry.value().ray.d - rd.d) / eps;
                    break;
                }
            }

            // Return approximate ray differential and weight
            rd.hasDifferentials = rx && ry;
            return new CameraRayDifferential(rd, cr.value().weight);
        }

        protected Ray RenderFromCamera(Ray r) { return cameraTransform.RenderFromCamera(r); }

        protected RayDifferential RenderFromCamera(RayDifferential r) { return cameraTransform.RenderFromCamera(r); }

        protected Vector3f RenderFromCamera(Vector3f v, Float time) { return cameraTransform.RenderFromCamera(v, time); }

        protected Normal3f RenderFromCamera(Normal3f v, Float time) { return cameraTransform.RenderFromCamera(v, time); }

        protected Point3f RenderFromCamera(Point3f p, Float time) { return cameraTransform.RenderFromCamera(p, time); }

        protected Vector3f CameraFromRender(Vector3f v, Float time) { return cameraTransform.CameraFromRender(v, time); }

        protected Normal3f CameraFromRender(Normal3f v, Float time) { return cameraTransform.CameraFromRender(v, time); }

        protected Point3f CameraFromRender(Point3f p, Float time) { return cameraTransform.CameraFromRender(p, time); }

        protected void FindMinimumDifferentials(Camera camera)
        {
            minPosDifferentialX = minPosDifferentialY = minDirDifferentialX =
                minDirDifferentialY = new Vector3f(pbrt.Infinity, pbrt.Infinity, pbrt.Infinity);

            CameraSample sample = default;
            sample.filterWeight = 1;
            sample.pLens = new Point2f(0.5f, 0.5f);
            sample.time = 0.5f;
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);

            int n = 512;
            for (int i = 0; i < n; ++i)
            {
                sample.pFilm.x = (Float)i / (n - 1) * film.FullResolution().x;
                sample.pFilm.y = (Float)i / (n - 1) * film.FullResolution().y;

                pstd.optional<CameraRayDifferential> crd = camera.GenerateRayDifferential(sample, ref lambda);
                if (!crd)
                    continue;

                RayDifferential ray = crd.value().ray;
                Vector3f dox = CameraFromRender(ray.rxOrigin - ray.o, ray.time);
                if (pbrt.Length(dox) < pbrt.Length(minPosDifferentialX))
                    minPosDifferentialX = dox;
                Vector3f doy = CameraFromRender(ray.ryOrigin - ray.o, ray.time);
                if (pbrt.Length(doy) < pbrt.Length(minPosDifferentialY))
                    minPosDifferentialY = doy;

                ray.d = pbrt.Normalize(ray.d);
                ray.rxDirection = pbrt.Normalize(ray.rxDirection);
                ray.ryDirection = pbrt.Normalize(ray.ryDirection);

                Frame f = Frame.FromZ(ray.d);
                Vector3f df = f.ToLocal(ray.d);  // should be (0, 0, 1);
                Vector3f dxf = pbrt.Normalize(f.ToLocal(ray.rxDirection));
                Vector3f dyf = pbrt.Normalize(f.ToLocal(ray.ryDirection));

                if (pbrt.Length(dxf - df) < pbrt.Length(minDirDifferentialX))
                    minDirDifferentialX = dxf - df;
                if (pbrt.Length(dyf - df) < pbrt.Length(minDirDifferentialY))
                    minDirDifferentialY = dyf - df;
            }

            pbrt.LOG_VERBOSE("Camera min pos differentials: %s, %s", minPosDifferentialX, minPosDifferentialY);
            pbrt.LOG_VERBOSE("Camera min dir differentials: %s, %s", minDirDifferentialX, minDirDifferentialY);
        }
    }

    // ProjectiveCamera Definition
    public abstract unsafe class ProjectiveCamera : CameraBase
    {
        // ProjectiveCamera Public Methods
        protected ProjectiveCamera() { }

        public override void InitMetadata(ImageMetadata metadata)
        {
            metadata.cameraFromWorld = cameraTransform.CameraFromWorld(shutterOpen).GetMatrix();

            // TODO: double check this
            Transform NDCFromWorld = pbrt.Translate(new Vector3f(0.5f, 0.5f, 0.5f)) * pbrt.Scale(0.5f, 0.5f, 0.5f) *
                                     screenFromCamera * new Transform(metadata.cameraFromWorld.value());
            metadata.NDCFromWorld = NDCFromWorld.GetMatrix();

            base.InitMetadata(metadata);
        }

        protected string BaseToString()
        {
            return base.ToString() +
                   pbrt.StringPrintf("screenFromCamera: %s cameraFromRaster: %s " +
                                        "rasterFromScreen: %s screenFromRaster: %s " +
                                        "lensRadius: %f focalDistance: %f",
                                        screenFromCamera, cameraFromRaster, rasterFromScreen, screenFromRaster,
                                        lensRadius, focalDistance);
        }

        protected ProjectiveCamera(CameraBaseParameters baseParameters, Transform screenFromCamera, Bounds2f screenWindow,
                                   Float lensRadius, Float focalDistance)
            : base(baseParameters)
        {
            this.screenFromCamera = screenFromCamera;
            this.lensRadius = lensRadius;
            this.focalDistance = focalDistance;
            // Compute projective camera transformations
            // Compute projective camera screen transformations
            Transform NDCFromScreen =
                pbrt.Scale(1 / (screenWindow.pMax.x - screenWindow.pMin.x),
                                1 / (screenWindow.pMax.y - screenWindow.pMin.y), 1) *
                pbrt.Translate(new Vector3f(-screenWindow.pMin.x, -screenWindow.pMax.y, 0));
            Transform rasterFromNDC = pbrt.Scale(film.FullResolution().x, -film.FullResolution().y, 1);
            rasterFromScreen = rasterFromNDC * NDCFromScreen;
            screenFromRaster = pbrt.Inverse(rasterFromScreen);

            cameraFromRaster = pbrt.Inverse(screenFromCamera) * screenFromRaster;
        }

        // ProjectiveCamera Protected Members
        protected Transform screenFromCamera, cameraFromRaster;
        protected Transform rasterFromScreen, screenFromRaster;
        protected Float lensRadius, focalDistance;
    }

    // OrthographicCamera Definition
    public sealed unsafe class OrthographicCamera : ProjectiveCamera
    {
        // OrthographicCamera Public Methods
        public OrthographicCamera(CameraBaseParameters baseParameters, Bounds2f screenWindow, Float lensRadius,
                                  Float focalDist)
            : base(baseParameters, pbrt.Orthographic(0, 1), screenWindow, lensRadius, focalDist)
        {
            // Compute differential changes in origin for orthographic camera rays
            dxCamera = cameraFromRaster.Apply(new Vector3f(1, 0, 0));
            dyCamera = cameraFromRaster.Apply(new Vector3f(0, 1, 0));

            // Compute minimum differentials for orthographic camera
            minDirDifferentialX = minDirDifferentialY = new Vector3f(0, 0, 0);
            minPosDifferentialX = dxCamera;
            minPosDifferentialY = dyCamera;
        }

        // OrthographicCamera Method Definitions
        public override pstd.optional<CameraRay> GenerateRay(CameraSample sample, ref SampledWavelengths lambda)
        {
            // Compute raster and camera sample positions
            Point3f pFilm = new Point3f(sample.pFilm.x, sample.pFilm.y, 0);
            Point3f pCamera = cameraFromRaster.Apply(pFilm);

            Ray ray = new Ray(pCamera, new Vector3f(0, 0, 1), SampleTime(sample.time), medium);
            // Modify ray for depth of field
            if (lensRadius > 0)
            {
                // Sample point on lens
                Point2f pLens = lensRadius * pbrt.SampleUniformDiskConcentric(sample.pLens);

                // Compute point on plane of focus
                Float ft = focalDistance / ray.d.z;
                Point3f pFocus = ray.Apply(ft);

                // Update ray for effect of lens
                ray.o = new Point3f(pLens.x, pLens.y, 0);
                ray.d = pbrt.Normalize(pFocus - ray.o);
            }

            return new CameraRay(RenderFromCamera(ray));
        }

        public override pstd.optional<CameraRayDifferential> GenerateRayDifferential(CameraSample sample,
                                                                                     ref SampledWavelengths lambda)
        {
            // Compute main orthographic viewing ray
            // Compute raster and camera sample positions
            Point3f pFilm = new Point3f(sample.pFilm.x, sample.pFilm.y, 0);
            Point3f pCamera = cameraFromRaster.Apply(pFilm);

            RayDifferential ray = new RayDifferential(pCamera, new Vector3f(0, 0, 1), SampleTime(sample.time), medium);
            // Modify ray for depth of field
            if (lensRadius > 0)
            {
                // Sample point on lens
                Point2f pLens = lensRadius * pbrt.SampleUniformDiskConcentric(sample.pLens);

                // Compute point on plane of focus
                Float ft = focalDistance / ray.d.z;
                Point3f pFocus = ray.Apply(ft);

                // Update ray for effect of lens
                ray.o = new Point3f(pLens.x, pLens.y, 0);
                ray.d = pbrt.Normalize(pFocus - ray.o);
            }

            // Compute ray differentials for _OrthographicCamera_
            if (lensRadius > 0)
            {
                // Compute _OrthographicCamera_ ray differentials accounting for lens
                // Sample point on lens
                Point2f pLens = lensRadius * pbrt.SampleUniformDiskConcentric(sample.pLens);

                Float ft = focalDistance / ray.d.z;
                Point3f pFocus = pCamera + dxCamera + (ft * new Vector3f(0, 0, 1));
                ray.rxOrigin = new Point3f(pLens.x, pLens.y, 0);
                ray.rxDirection = pbrt.Normalize(pFocus - ray.rxOrigin);

                pFocus = pCamera + dyCamera + (ft * new Vector3f(0, 0, 1));
                ray.ryOrigin = new Point3f(pLens.x, pLens.y, 0);
                ray.ryDirection = pbrt.Normalize(pFocus - ray.ryOrigin);
            }
            else
            {
                ray.rxOrigin = ray.o + dxCamera;
                ray.ryOrigin = ray.o + dyCamera;
                ray.rxDirection = ray.ryDirection = ray.d;
            }

            ray.hasDifferentials = true;
            return new CameraRayDifferential(RenderFromCamera(ray));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ OrthographicCamera %s dxCamera: %s dyCamera: %s ]", BaseToString(), dxCamera,
                                        dyCamera);
        }

        public static OrthographicCamera Create(ParameterDictionary parameters, CameraTransform cameraTransform,
                                                Film film, Medium medium, FileLoc loc, Allocator alloc = null)
        {
            CameraBaseParameters cameraBaseParameters = new CameraBaseParameters(cameraTransform, film, medium,
                                                                                 parameters, loc);

            Float lensradius = parameters.GetOneFloat("lensradius", 0.0f);
            Float focaldistance = parameters.GetOneFloat("focaldistance", 1e6f);
            Float frame = parameters.GetOneFloat("frameaspectratio",
                                                 (Float)film.FullResolution().x / (Float)film.FullResolution().y);
            Bounds2f screen = new Bounds2f();
            if (frame > 1.0f)
            {
                screen.pMin.x = -frame;
                screen.pMax.x = frame;
                screen.pMin.y = -1.0f;
                screen.pMax.y = 1.0f;
            }
            else
            {
                screen.pMin.x = -1.0f;
                screen.pMax.x = 1.0f;
                screen.pMin.y = -1.0f / frame;
                screen.pMax.y = 1.0f / frame;
            }
            List<Float> sw = parameters.GetFloatArray("screenwindow");
            if (sw.Count != 0)
            {
                if (pbrt.Options.fullscreen)
                {
                    pbrt.Warning("\"screenwindow\" is ignored in fullscreen mode");
                }
                else
                {
                    if (sw.Count == 4)
                    {
                        screen.pMin.x = sw[0];
                        screen.pMax.x = sw[1];
                        screen.pMin.y = sw[2];
                        screen.pMax.y = sw[3];
                    }
                    else
                    {
                        pbrt.Error("\"screenwindow\" should have four values");
                    }
                }
            }
            return new OrthographicCamera(cameraBaseParameters, screen, lensradius, focaldistance);
        }

        public override SampledSpectrum We(Ray ray, ref SampledWavelengths lambda, Point2f* pRaster2 = null)
        {
            pbrt.LOG_FATAL("We() unimplemented for OrthographicCamera");
            return default;
        }

        public override void PDF_We(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            pbrt.LOG_FATAL("PDF_We() unimplemented for OrthographicCamera");
        }

        public override pstd.optional<CameraWiSample> SampleWi(Interaction reference, Point2f u, ref SampledWavelengths lambda)
        {
            pbrt.LOG_FATAL("SampleWi() unimplemented for OrthographicCamera");
            return default;
        }

        // OrthographicCamera Private Members
        Vector3f dxCamera, dyCamera;
    }

    // PerspectiveCamera Definition
    public sealed unsafe class PerspectiveCamera : ProjectiveCamera
    {
        // PerspectiveCamera Public Methods
        public PerspectiveCamera(CameraBaseParameters baseParameters, Float fov, Bounds2f screenWindow, Float lensRadius,
                                 Float focalDist)
            : base(baseParameters, pbrt.Perspective(fov, 1e-2f, 1000.0f), screenWindow, lensRadius, focalDist)
        {
            // Compute differential changes in origin for perspective camera rays
            dxCamera = cameraFromRaster.Apply(new Point3f(1, 0, 0)) - cameraFromRaster.Apply(new Point3f(0, 0, 0));
            dyCamera = cameraFromRaster.Apply(new Point3f(0, 1, 0)) - cameraFromRaster.Apply(new Point3f(0, 0, 0));

            // Compute _cosTotalWidth_ for perspective camera
            Vector2f filterRadius = film.GetFilter().Radius();
            Point2f radius = new Point2f(filterRadius.x, filterRadius.y);
            Point3f pCorner = new Point3f(-radius.x, -radius.y, 0.0f);
            Vector3f wCornerCamera = pbrt.Normalize((Vector3f)cameraFromRaster.Apply(pCorner));
            cosTotalWidth = wCornerCamera.z;
            pbrt.DCHECK(.9999 * cosTotalWidth < std.cos(pbrt.Radians(fov / 2)));

            // Compute image plane area at $z=1$ for _PerspectiveCamera_
            Point2i res = film.FullResolution();
            Point3f pMin = cameraFromRaster.Apply(new Point3f(0, 0, 0));
            Point3f pMax = cameraFromRaster.Apply(new Point3f(res.x, res.y, 0));
            pMin /= pMin.z;
            pMax /= pMax.z;
            A = std.abs((pMax.x - pMin.x) * (pMax.y - pMin.y));

            // Compute minimum differentials for _PerspectiveCamera_
            FindMinimumDifferentials(this);
        }

        // PerspectiveCamera Method Definitions
        public override pstd.optional<CameraRay> GenerateRay(CameraSample sample, ref SampledWavelengths lambda)
        {
            // Compute raster and camera sample positions
            Point3f pFilm = new Point3f(sample.pFilm.x, sample.pFilm.y, 0);
            Point3f pCamera = cameraFromRaster.Apply(pFilm);

            Ray ray = new Ray(new Point3f(0, 0, 0), pbrt.Normalize((Vector3f)pCamera), SampleTime(sample.time), medium);
            // Modify ray for depth of field
            if (lensRadius > 0)
            {
                // Sample point on lens
                Point2f pLens = lensRadius * pbrt.SampleUniformDiskConcentric(sample.pLens);

                // Compute point on plane of focus
                Float ft = focalDistance / ray.d.z;
                Point3f pFocus = ray.Apply(ft);

                // Update ray for effect of lens
                ray.o = new Point3f(pLens.x, pLens.y, 0);
                ray.d = pbrt.Normalize(pFocus - ray.o);
            }

            return new CameraRay(RenderFromCamera(ray));
        }

        public override pstd.optional<CameraRayDifferential> GenerateRayDifferential(CameraSample sample,
                                                                                     ref SampledWavelengths lambda)
        {
            // Compute raster and camera sample positions
            Point3f pFilm = new Point3f(sample.pFilm.x, sample.pFilm.y, 0);
            Point3f pCamera = cameraFromRaster.Apply(pFilm);
            Vector3f dir = pbrt.Normalize(new Vector3f(pCamera.x, pCamera.y, pCamera.z));
            RayDifferential ray = new RayDifferential(new Point3f(0, 0, 0), dir, SampleTime(sample.time), medium);
            // Modify ray for depth of field
            if (lensRadius > 0)
            {
                // Sample point on lens
                Point2f pLens = lensRadius * pbrt.SampleUniformDiskConcentric(sample.pLens);

                // Compute point on plane of focus
                Float ft = focalDistance / ray.d.z;
                Point3f pFocus = ray.Apply(ft);

                // Update ray for effect of lens
                ray.o = new Point3f(pLens.x, pLens.y, 0);
                ray.d = pbrt.Normalize(pFocus - ray.o);
            }

            // Compute offset rays for _PerspectiveCamera_ ray differentials
            if (lensRadius > 0)
            {
                // Compute _PerspectiveCamera_ ray differentials accounting for lens
                // Sample point on lens
                Point2f pLens = lensRadius * pbrt.SampleUniformDiskConcentric(sample.pLens);

                // Compute $x$ ray differential for _PerspectiveCamera_ with lens
                Vector3f dx = pbrt.Normalize((Vector3f)(pCamera + dxCamera));
                Float ft = focalDistance / dx.z;
                Point3f pFocus = new Point3f(0, 0, 0) + (ft * dx);
                ray.rxOrigin = new Point3f(pLens.x, pLens.y, 0);
                ray.rxDirection = pbrt.Normalize(pFocus - ray.rxOrigin);

                // Compute $y$ ray differential for _PerspectiveCamera_ with lens
                Vector3f dy = pbrt.Normalize((Vector3f)(pCamera + dyCamera));
                ft = focalDistance / dy.z;
                pFocus = new Point3f(0, 0, 0) + (ft * dy);
                ray.ryOrigin = new Point3f(pLens.x, pLens.y, 0);
                ray.ryDirection = pbrt.Normalize(pFocus - ray.ryOrigin);
            }
            else
            {
                ray.rxOrigin = ray.ryOrigin = ray.o;
                ray.rxDirection = pbrt.Normalize((Vector3f)pCamera + dxCamera);
                ray.ryDirection = pbrt.Normalize((Vector3f)pCamera + dyCamera);
            }

            ray.hasDifferentials = true;
            return new CameraRayDifferential(RenderFromCamera(ray));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ PerspectiveCamera %s dxCamera: %s dyCamera: %s A: " +
                                        "%f cosTotalWidth: %f ]",
                                        BaseToString(), dxCamera, dyCamera, A, cosTotalWidth);
        }

        public static PerspectiveCamera Create(ParameterDictionary parameters, CameraTransform cameraTransform, Film film,
                                               Medium medium, FileLoc loc, Allocator alloc = null)
        {
            CameraBaseParameters cameraBaseParameters = new CameraBaseParameters(cameraTransform, film, medium,
                                                                                 parameters, loc);

            Float lensradius = parameters.GetOneFloat("lensradius", 0.0f);
            Float focaldistance = parameters.GetOneFloat("focaldistance", 1e6f);
            Float frame = parameters.GetOneFloat("frameaspectratio",
                                                 (Float)film.FullResolution().x / (Float)film.FullResolution().y);
            Bounds2f screen = new Bounds2f();
            if (frame > 1.0f)
            {
                screen.pMin.x = -frame;
                screen.pMax.x = frame;
                screen.pMin.y = -1.0f;
                screen.pMax.y = 1.0f;
            }
            else
            {
                screen.pMin.x = -1.0f;
                screen.pMax.x = 1.0f;
                screen.pMin.y = -1.0f / frame;
                screen.pMax.y = 1.0f / frame;
            }
            List<Float> sw = parameters.GetFloatArray("screenwindow");
            if (sw.Count != 0)
            {
                if (pbrt.Options.fullscreen)
                {
                    pbrt.Warning("\"screenwindow\" is ignored in fullscreen mode");
                }
                else
                {
                    if (sw.Count == 4)
                    {
                        screen.pMin.x = sw[0];
                        screen.pMax.x = sw[1];
                        screen.pMin.y = sw[2];
                        screen.pMax.y = sw[3];
                    }
                    else
                    {
                        pbrt.Error(loc, "\"screenwindow\" should have four values");
                    }
                }
            }
            Float fov = parameters.GetOneFloat("fov", 90.0f);
            return new PerspectiveCamera(cameraBaseParameters, fov, screen, lensradius, focaldistance);
        }

        public override SampledSpectrum We(Ray ray, ref SampledWavelengths lambda, Point2f* pRasterOut = null)
        {
            // Check if ray is forward-facing with respect to the camera
            Float cosTheta = pbrt.Dot(ray.d, RenderFromCamera(new Vector3f(0, 0, 1), ray.time));
            if (cosTheta <= cosTotalWidth)
                return new SampledSpectrum(0.0f);

            // Map ray $(\p{}, \w{})$ onto the raster grid
            Point3f pFocus = ray.Apply((lensRadius > 0 ? focalDistance : 1) / cosTheta);
            Point3f pCamera = CameraFromRender(pFocus, ray.time);
            Point3f pRaster = cameraFromRaster.ApplyInverse(pCamera);

            // Return raster position if requested
            if (pRasterOut != null)
                *pRasterOut = new Point2f(pRaster.x, pRaster.y);

            // Return zero importance for out of bounds points
            Bounds2f sampleBounds = film.SampleBounds();
            if (!pbrt.Inside(new Point2f(pRaster.x, pRaster.y), sampleBounds))
                return new SampledSpectrum(0.0f);

            // Compute lens area of perspective camera
            Float lensArea = lensRadius != 0 ? (Pi * pbrt.Sqr(lensRadius)) : 1;

            // Return importance for point on image plane
            return new SampledSpectrum(1 / (A * lensArea * pbrt.Pow(4, cosTheta)));
        }

        public override void PDF_We(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            // Return zero PDF values if ray direction is not front-facing
            Float cosTheta = pbrt.Dot(ray.d, RenderFromCamera(new Vector3f(0, 0, 1), ray.time));
            if (cosTheta <= cosTotalWidth)
            {
                *pdfPos = *pdfDir = 0;
                return;
            }

            // Map ray $(\p{}, \w{})$ onto the raster grid
            Point3f pFocus = ray.Apply((lensRadius > 0 ? focalDistance : 1) / cosTheta);
            Point3f pCamera = CameraFromRender(pFocus, ray.time);
            Point3f pRaster = cameraFromRaster.ApplyInverse(pCamera);

            // Return zero probability for out of bounds points
            Bounds2f sampleBounds = film.SampleBounds();
            if (!pbrt.Inside(new Point2f(pRaster.x, pRaster.y), sampleBounds))
            {
                *pdfPos = *pdfDir = 0;
                return;
            }

            // Compute lens area  and return perspective camera probabilities
            Float lensArea = lensRadius != 0 ? (Pi * pbrt.Sqr(lensRadius)) : 1;
            *pdfPos = 1 / lensArea;
            *pdfDir = 1 / (A * pbrt.Pow(3, cosTheta));
        }

        public override pstd.optional<CameraWiSample> SampleWi(Interaction reference, Point2f u, ref SampledWavelengths lambda)
        {
            // Uniformly sample a lens interaction _lensIntr_
            Point2f pLens = lensRadius * pbrt.SampleUniformDiskConcentric(u);
            Point3f pLensRender = RenderFromCamera(new Point3f(pLens.x, pLens.y, 0), reference.time);
            Normal3f n = (Normal3f)RenderFromCamera(new Vector3f(0, 0, 1), reference.time);
            Interaction lensIntr = new Interaction(pLensRender, n, reference.time, medium);

            // Find incident direction to camera _wi_ at _ref_
            Vector3f wi = lensIntr.p() - reference.p();
            Float dist = pbrt.Length(wi);
            wi /= dist;

            // Compute PDF for importance arriving at _ref_
            Float lensArea = lensRadius != 0 ? (Pi * pbrt.Sqr(lensRadius)) : 1;
            Float pdf = pbrt.Sqr(dist) / (pbrt.AbsDot(lensIntr.n, wi) * lensArea);

            // Compute importance and return _CameraWiSample_
            Point2f pRaster;
            SampledSpectrum Wi = We(new Ray(lensIntr.SpawnRay(-wi)), ref lambda, &pRaster);
            if (!Wi)
                return default;
            return new CameraWiSample(Wi, wi, pdf, pRaster, reference, lensIntr);
        }

        // PerspectiveCamera Private Members
        Vector3f dxCamera, dyCamera;
        Float cosTotalWidth;
        Float A;
    }

    // SphericalCamera Definition
    public sealed unsafe class SphericalCamera : CameraBase
    {
        // SphericalCamera::Mapping Definition
        public enum Mapping { EquiRectangular, EqualArea }

        // SphericalCamera Public Methods
        public SphericalCamera(CameraBaseParameters baseParameters, Mapping mapping)
            : base(baseParameters)
        {
            this.mapping = mapping;
            // Compute minimum differentials for _SphericalCamera_
            FindMinimumDifferentials(this);
        }

        public override pstd.optional<CameraRayDifferential> GenerateRayDifferential(CameraSample sample,
                                                                                     ref SampledWavelengths lambda)
        {
            return CameraBase.GenerateRayDifferential(this, sample, ref lambda);
        }

        public override SampledSpectrum We(Ray ray, ref SampledWavelengths lambda, Point2f* pRaster2 = null)
        {
            pbrt.LOG_FATAL("We() unimplemented for SphericalCamera");
            return default;
        }

        public override void PDF_We(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            pbrt.LOG_FATAL("PDF_We() unimplemented for SphericalCamera");
        }

        public override pstd.optional<CameraWiSample> SampleWi(Interaction reference, Point2f u, ref SampledWavelengths lambda)
        {
            pbrt.LOG_FATAL("SampleWi() unimplemented for SphericalCamera");
            return default;
        }

        // SphericalCamera Method Definitions
        public override pstd.optional<CameraRay> GenerateRay(CameraSample sample, ref SampledWavelengths lambda)
        {
            // Compute spherical camera ray direction
            Point2f uv = new Point2f(sample.pFilm.x / film.FullResolution().x, sample.pFilm.y / film.FullResolution().y);
            Vector3f dir;
            if (mapping == Mapping.EquiRectangular)
            {
                // Compute ray direction using equirectangular mapping
                Float theta = Pi * uv[1], phi = 2 * Pi * uv[0];
                dir = pbrt.SphericalDirection(std.sin(theta), std.cos(theta), phi);
            }
            else
            {
                // Compute ray direction using equal area mapping
                uv = pbrt.WrapEqualAreaSquare(uv);
                dir = pbrt.EqualAreaSquareToSphere(uv);
            }
            pstd.swap(ref dir.y, ref dir.z);

            Ray ray = new Ray(new Point3f(0, 0, 0), dir, SampleTime(sample.time), medium);
            return new CameraRay(RenderFromCamera(ray));
        }

        public static SphericalCamera Create(ParameterDictionary parameters, CameraTransform cameraTransform, Film film,
                                             Medium medium, FileLoc loc, Allocator alloc = null)
        {
            CameraBaseParameters cameraBaseParameters = new CameraBaseParameters(cameraTransform, film, medium,
                                                                                 parameters, loc);

            Float lensradius = parameters.GetOneFloat("lensradius", 0.0f);
            Float focaldistance = parameters.GetOneFloat("focaldistance", 1e30f);
            Float frame = parameters.GetOneFloat("frameaspectratio",
                                                 (Float)film.FullResolution().x / (Float)film.FullResolution().y);
            Bounds2f screen = new Bounds2f();
            if (frame > 1.0f)
            {
                screen.pMin.x = -frame;
                screen.pMax.x = frame;
                screen.pMin.y = -1.0f;
                screen.pMax.y = 1.0f;
            }
            else
            {
                screen.pMin.x = -1.0f;
                screen.pMax.x = 1.0f;
                screen.pMin.y = -1.0f / frame;
                screen.pMax.y = 1.0f / frame;
            }
            List<Float> sw = parameters.GetFloatArray("screenwindow");
            if (sw.Count != 0)
            {
                if (pbrt.Options.fullscreen)
                {
                    pbrt.Warning("\"screenwindow\" is ignored in fullscreen mode");
                }
                else
                {
                    if (sw.Count == 4)
                    {
                        screen.pMin.x = sw[0];
                        screen.pMax.x = sw[1];
                        screen.pMin.y = sw[2];
                        screen.pMax.y = sw[3];
                    }
                    else
                    {
                        pbrt.Error(loc, "\"screenwindow\" should have four values");
                    }
                }
            }
            // (void)lensradius;     // don't need this
            // (void)focaldistance;  // don't need this

            string m = parameters.GetOneString("mapping", "equalarea");
            Mapping mapping;
            if (m == "equalarea")
                mapping = Mapping.EqualArea;
            else if (m == "equirectangular")
                mapping = Mapping.EquiRectangular;
            else
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unknown mapping for spherical camera. (Must be " +
                                                            "\"equalarea\" or \"equirectangular\".)", m));
                return null;
            }

            return new SphericalCamera(cameraBaseParameters, mapping);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SphericalCamera %s mapping: %s ]", base.ToString(),
                                        mapping == Mapping.EquiRectangular ? "EquiRectangular" : "EqualArea");
        }

        // SphericalCamera Private Members
        Mapping mapping;
    }

    // ExitPupilSample Definition
    public struct ExitPupilSample
    {
        public Point3f pPupil;
        public Float pdf;
    }

    // RealisticCamera Definition
    public sealed unsafe class RealisticCamera : CameraBase
    {
        // RealisticCamera Private Declarations
        struct LensElementInterface
        {
            public Float curvatureRadius;
            public Float thickness;
            public Float eta;
            public Float apertureRadius;

            public override string ToString()
            {
                return pbrt.StringPrintf("[ LensElementInterface curvatureRadius: %f thickness: %f " +
                                            "eta: %f apertureRadius: %f ]",
                                            curvatureRadius, thickness, eta, apertureRadius);
            }
        }

        // RealisticCamera Method Definitions
        public RealisticCamera(CameraBaseParameters baseParameters, List<Float> lensParameters, Float focusDistance,
                               Float setApertureDiameter, Image apertureImage, Allocator alloc)
            : base(baseParameters)
        {
            elementInterfaces = new pstd.vector<LensElementInterface>();
            exitPupilBounds = new pstd.vector<Bounds2f>();
            this.apertureImage = apertureImage;

            // Compute film's physical extent
            Float aspect = (Float)film.FullResolution().y / (Float)film.FullResolution().x;
            Float diagonal = film.Diagonal();
            Float x = std.sqrt(pbrt.Sqr(diagonal) / (1 + pbrt.Sqr(aspect)));
            Float y = aspect * x;
            physicalExtent = new Bounds2f(new Point2f(-x / 2, -y / 2), new Point2f(x / 2, y / 2));

            // Initialize _elementInterfaces_ for camera
            for (int i = 0; i < lensParameters.Count; i += 4)
            {
                // Extract lens element configuration from _lensParameters_
                Float curvatureRadius = lensParameters[i] / 1000;
                Float thickness = lensParameters[i + 1] / 1000;
                Float eta = lensParameters[i + 2];
                Float apertureDiameter = lensParameters[i + 3] / 1000;

                if (curvatureRadius == 0)
                {
                    // Set aperture stop diameter
                    setApertureDiameter /= 1000;
                    if (setApertureDiameter > apertureDiameter)
                        pbrt.Warning(pbrt.StringPrintf("Aperture diameter %f is greater than maximum possible %f. " +
                                                             "Clamping it.", setApertureDiameter, apertureDiameter));
                    else
                        apertureDiameter = setApertureDiameter;
                }
                // Add element interface to end of _elementInterfaces_
                elementInterfaces.push_back(new LensElementInterface
                {
                    curvatureRadius = curvatureRadius,
                    thickness = thickness,
                    eta = eta,
                    apertureRadius = apertureDiameter / 2
                });
            }

            // Compute lens--film distance for given focus distance
            elementInterfaces.back().thickness = FocusThickLens(focusDistance);

            // Compute exit pupil bounds at sampled points on the film
            int nSamples = 64;
            exitPupilBounds.resize((ulong)nSamples);
            pbrt.ParallelFor(0, nSamples, (int i) =>
            {
                Float r0 = (Float)i / nSamples * film.Diagonal() / 2;
                Float r1 = (Float)(i + 1) / nSamples * film.Diagonal() / 2;
                exitPupilBounds[(ulong)i] = BoundExitPupil(r0, r1);
            });

            // Compute minimum differentials for _RealisticCamera_
            FindMinimumDifferentials(this);
        }

        public override pstd.optional<CameraRayDifferential> GenerateRayDifferential(CameraSample sample,
                                                                                     ref SampledWavelengths lambda)
        {
            return CameraBase.GenerateRayDifferential(this, sample, ref lambda);
        }

        public override SampledSpectrum We(Ray ray, ref SampledWavelengths lambda, Point2f* pRaster2 = null)
        {
            pbrt.LOG_FATAL("We() unimplemented for RealisticCamera");
            return default;
        }

        public override void PDF_We(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            pbrt.LOG_FATAL("PDF_We() unimplemented for RealisticCamera");
        }

        public override pstd.optional<CameraWiSample> SampleWi(Interaction reference, Point2f u, ref SampledWavelengths lambda)
        {
            pbrt.LOG_FATAL("SampleWi() unimplemented for RealisticCamera");
            return default;
        }

        // RealisticCamera Private Methods
        Float LensRearZ() { return elementInterfaces.back().thickness; }

        Float LensFrontZ()
        {
            Float zSum = 0;
            for (ulong i = 0; i < elementInterfaces.size(); ++i)
                zSum += elementInterfaces[i].thickness;
            return zSum;
        }

        Float RearElementRadius() { return elementInterfaces.back().apertureRadius; }

        static bool IntersectSphericalElement(Float radius, Float zCenter, Ray ray, Float* t, Normal3f* n)
        {
            // Compute _t0_ and _t1_ for ray--element intersection
            Point3f o = ray.o - new Vector3f(0, 0, zCenter);
            Float A = ray.d.x * ray.d.x + ray.d.y * ray.d.y + ray.d.z * ray.d.z;
            Float B = 2 * (ray.d.x * o.x + ray.d.y * o.y + ray.d.z * o.z);
            Float C = o.x * o.x + o.y * o.y + o.z * o.z - radius * radius;
            Float t0, t1;
            if (!pbrt.Quadratic(A, B, C, &t0, &t1))
                return false;

            // Select intersection $t$ based on ray direction and element curvature
            bool useCloserT = (ray.d.z > 0) ^ (radius < 0);
            *t = useCloserT ? std.min(t0, t1) : std.max(t0, t1);
            if (*t < 0)
                return false;

            // Compute surface normal of element at ray intersection point
            Point3f hit = o + *t * ray.d;
            *n = new Normal3f(hit.x, hit.y, hit.z);
            *n = pbrt.FaceForward(pbrt.Normalize(*n), -ray.d);

            return true;
        }

        Float TraceLensesFromFilm(Ray rCamera, Ray* rOut)
        {
            Float elementZ = 0, weight = 1;
            // Transform _rCamera_ from camera to lens system space
            Ray rLens = new Ray(new Point3f(rCamera.o.x, rCamera.o.y, -rCamera.o.z),
                                new Vector3f(rCamera.d.x, rCamera.d.y, -rCamera.d.z), rCamera.time);

            for (int i = (int)elementInterfaces.size() - 1; i >= 0; --i)
            {
                LensElementInterface element = elementInterfaces[(ulong)i];
                // Update ray from film accounting for interaction with _element_
                elementZ -= element.thickness;
                // Compute intersection of ray with lens element
                Float t;
                Normal3f n = default;
                bool isStop = (element.curvatureRadius == 0);
                if (isStop)
                {
                    // Compute _t_ at plane of aperture stop
                    t = (elementZ - rLens.o.z) / rLens.d.z;
                    if (t < 0)
                        return 0;
                }
                else
                {
                    // Intersect ray with element to compute _t_ and _n_
                    Float radius = element.curvatureRadius;
                    Float zCenter = elementZ + element.curvatureRadius;
                    if (!IntersectSphericalElement(radius, zCenter, rLens, &t, &n))
                        return 0;
                }
                pbrt.DCHECK_GE(t, 0);

                // Test intersection point against element aperture
                Point3f pHit = rLens.Apply(t);
                if (isStop && apertureImage)
                {
                    // Check intersection point against _apertureImage_
                    Point2f uv = new Point2f((pHit.x / element.apertureRadius + 1) / 2,
                                             (pHit.y / element.apertureRadius + 1) / 2);
                    weight = apertureImage.BilerpChannel(uv, 0, WrapMode.Black);
                    if (weight == 0)
                        return 0;
                }
                else
                {
                    // Check intersection point against spherical aperture
                    if (pbrt.Sqr(pHit.x) + pbrt.Sqr(pHit.y) > pbrt.Sqr(element.apertureRadius))
                        return 0;
                }
                rLens.o = pHit;

                // Update ray path for element interface interaction
                if (!isStop)
                {
                    Vector3f w;
                    Float eta_i = element.eta;
                    Float eta_t = (i > 0 && elementInterfaces[(ulong)(i - 1)].eta != 0)
                                      ? elementInterfaces[(ulong)(i - 1)].eta
                                      : 1;
                    if (!pbrt.Refract(pbrt.Normalize(-rLens.d), n, eta_t / eta_i, null, &w))
                        return 0;
                    rLens.d = w;
                }
            }
            // Transform lens system space ray back to camera space
            if (rOut != null)
                *rOut = new Ray(new Point3f(rLens.o.x, rLens.o.y, -rLens.o.z),
                                new Vector3f(rLens.d.x, rLens.d.y, -rLens.d.z), rLens.time);

            return weight;
        }

        Float TraceLensesFromScene(Ray rCamera, Ray* rOut)
        {
            Float elementZ = -LensFrontZ();
            // Transform _rCamera_ from camera to lens system space
            Transform LensFromCamera = pbrt.Scale(1, 1, -1);
            Ray rLens = LensFromCamera.Apply(rCamera);
            for (int i = 0; i < (int)elementInterfaces.size(); ++i)
            {
                LensElementInterface element = elementInterfaces[(ulong)i];
                // Compute intersection of ray with lens element
                Float t;
                Normal3f n = default;
                bool isStop = (element.curvatureRadius == 0);
                if (isStop)
                {
                    t = (elementZ - rLens.o.z) / rLens.d.z;
                    if (t < 0)
                        return 0;
                }
                else
                {
                    Float radius = element.curvatureRadius;
                    Float zCenter = elementZ + element.curvatureRadius;
                    if (!IntersectSphericalElement(radius, zCenter, rLens, &t, &n))
                        return 0;
                }

                // Test intersection point against element aperture
                // Don't worry about the aperture image here.
                Point3f pHit = rLens.Apply(t);
                Float r2 = pHit.x * pHit.x + pHit.y * pHit.y;
                if (r2 > element.apertureRadius * element.apertureRadius)
                    return 0;
                rLens.o = pHit;

                // Update ray path for from-scene element interface interaction
                if (!isStop)
                {
                    Vector3f wt;
                    Float eta_i = (i == 0 || elementInterfaces[(ulong)(i - 1)].eta == 0)
                                      ? 1
                                      : elementInterfaces[(ulong)(i - 1)].eta;
                    Float eta_t = (elementInterfaces[(ulong)i].eta != 0) ? elementInterfaces[(ulong)i].eta : 1;
                    if (!pbrt.Refract(pbrt.Normalize(-rLens.d), n, eta_t / eta_i, null, &wt))
                        return 0;
                    rLens.d = wt;
                }
                elementZ += element.thickness;
            }
            // Transform _rLens_ from lens system space back to camera space
            if (rOut != null)
                *rOut = new Ray(new Point3f(rLens.o.x, rLens.o.y, -rLens.o.z),
                                new Vector3f(rLens.d.x, rLens.d.y, -rLens.d.z), rLens.time);
            return 1;
        }

        // (C's printf/fprintf(stderr, ...) for the debugging output below)
        static void printf(string fmt, params object[] args)
        {
            Console.Out.Write(pbrt.StringPrintf(fmt, args));
        }

        public void DrawLensSystem()
        {
            Float sumz = -LensFrontZ();
            Float z = sumz;
            for (int i = 0; i < (int)elementInterfaces.size(); ++i)
            {
                LensElementInterface element = elementInterfaces[(ulong)i];
                Float r = element.curvatureRadius;
                if (r == 0)
                {
                    // stop
                    printf("{Thick, Line[{{%f, %f}, {%f, %f}}], ", z, element.apertureRadius, z,
                           2 * element.apertureRadius);
                    printf("Line[{{%f, %f}, {%f, %f}}]}, ", z, -element.apertureRadius, z,
                           -2 * element.apertureRadius);
                }
                else
                {
                    Float theta = std.abs(pbrt.SafeASin(element.apertureRadius / r));
                    if (r > 0)
                    {
                        // convex as seen from front of lens
                        Float t0 = pbrt.Pi - theta;
                        Float t1 = pbrt.Pi + theta;
                        printf("Circle[{%f, 0}, %f, {%f, %f}], ", z + r, r, t0, t1);
                    }
                    else
                    {
                        // concave as seen from front of lens
                        Float t0 = -theta;
                        Float t1 = theta;
                        printf("Circle[{%f, 0}, %f, {%f, %f}], ", z + r, -r, t0, t1);
                    }
                    if (element.eta != 0 && element.eta != 1)
                    {
                        // connect top/bottom to next element
                        pbrt.CHECK_LT((ulong)(i + 1), elementInterfaces.size());
                        Float nextApertureRadius = elementInterfaces[(ulong)(i + 1)].apertureRadius;
                        Float h = std.max(element.apertureRadius, nextApertureRadius);
                        Float hlow = std.min(element.apertureRadius, nextApertureRadius);

                        Float zp0, zp1;
                        if (r > 0)
                        {
                            zp0 = z + element.curvatureRadius -
                                  element.apertureRadius / std.tan(theta);
                        }
                        else
                        {
                            zp0 = z + element.curvatureRadius +
                                  element.apertureRadius / std.tan(theta);
                        }

                        Float nextCurvatureRadius = elementInterfaces[(ulong)(i + 1)].curvatureRadius;
                        Float nextTheta =
                            std.abs(pbrt.SafeASin(nextApertureRadius / nextCurvatureRadius));
                        if (nextCurvatureRadius > 0)
                        {
                            zp1 = z + element.thickness + nextCurvatureRadius -
                                  nextApertureRadius / std.tan(nextTheta);
                        }
                        else
                        {
                            zp1 = z + element.thickness + nextCurvatureRadius +
                                  nextApertureRadius / std.tan(nextTheta);
                        }

                        // Connect tops
                        printf("Line[{{%f, %f}, {%f, %f}}], ", zp0, h, zp1, h);
                        printf("Line[{{%f, %f}, {%f, %f}}], ", zp0, -h, zp1, -h);

                        // vertical lines when needed to close up the element profile
                        if (element.apertureRadius < nextApertureRadius)
                        {
                            printf("Line[{{%f, %f}, {%f, %f}}], ", zp0, h, zp0, hlow);
                            printf("Line[{{%f, %f}, {%f, %f}}], ", zp0, -h, zp0, -hlow);
                        }
                        else if (element.apertureRadius > nextApertureRadius)
                        {
                            printf("Line[{{%f, %f}, {%f, %f}}], ", zp1, h, zp1, hlow);
                            printf("Line[{{%f, %f}, {%f, %f}}], ", zp1, -h, zp1, -hlow);
                        }
                    }
                }
                z += element.thickness;
            }

            // 24mm height for 35mm film
            printf("Line[{{0, -.012}, {0, .012}}], ");
            // optical axis
            printf("Line[{{0, 0}, {%f, 0}}] ", 1.2f * sumz);
        }

        public void DrawRayPathFromFilm(Ray r, bool arrow, bool toOpticalIntercept)
        {
            Float elementZ = 0;
            // Transform _ray_ from camera to lens system space
            Transform LensFromCamera = pbrt.Scale(1, 1, -1);
            Ray ray = LensFromCamera.Apply(r);
            printf("{ ");
            if (TraceLensesFromFilm(r, null) == 0)
            {
                printf("Dashed, RGBColor[.8, .5, .5]");
            }
            else
                printf("RGBColor[.5, .5, .8]");

            for (int i = (int)elementInterfaces.size() - 1; i >= 0; --i)
            {
                LensElementInterface element = elementInterfaces[(ulong)i];
                elementZ -= element.thickness;
                bool isStop = (element.curvatureRadius == 0);
                // Compute intersection of ray with lens element
                Float t;
                Normal3f n = default;
                if (isStop)
                    t = -(ray.o.z - elementZ) / ray.d.z;
                else
                {
                    Float radius = element.curvatureRadius;
                    Float zCenter = elementZ + element.curvatureRadius;
                    if (!IntersectSphericalElement(radius, zCenter, ray, &t, &n))
                        goto done;
                }
                pbrt.CHECK_GE(t, 0);

                printf(", Line[{{%f, %f}, {%f, %f}}]", ray.o.z, ray.o.x, ray.Apply(t).z, ray.Apply(t).x);

                // Test intersection point against element aperture
                Point3f pHit = ray.Apply(t);
                Float r2 = pHit.x * pHit.x + pHit.y * pHit.y;
                Float apertureRadius2 = element.apertureRadius * element.apertureRadius;
                if (r2 > apertureRadius2)
                    goto done;
                ray.o = pHit;

                // Update ray path for element interface interaction
                if (!isStop)
                {
                    Vector3f wt;
                    Float eta_i = element.eta;
                    Float eta_t = (i > 0 && elementInterfaces[(ulong)(i - 1)].eta != 0)
                                      ? elementInterfaces[(ulong)(i - 1)].eta
                                      : 1;
                    if (!pbrt.Refract(pbrt.Normalize(-ray.d), n, eta_t / eta_i, null, &wt))
                        goto done;
                    ray.d = wt;
                }
            }

            ray.d = pbrt.Normalize(ray.d);
            {
                Float ta = std.abs(elementZ / 4);
                if (toOpticalIntercept)
                {
                    ta = -ray.o.x / ray.d.x;
                    printf(", Point[{%f, %f}]", ray.Apply(ta).z, ray.Apply(ta).x);
                }
                printf(", %s[{{%f, %f}, {%f, %f}}]", arrow ? "Arrow" : "Line", ray.o.z, ray.o.x,
                       ray.Apply(ta).z, ray.Apply(ta).x);

                // overdraw the optical axis if needed...
                if (toOpticalIntercept)
                    printf(", Line[{{%f, 0}, {%f, 0}}]", ray.o.z, ray.Apply(ta).z * 1.05f);
            }

        done:
            printf("}");
        }

        public void DrawRayPathFromScene(Ray r, bool arrow, bool toOpticalIntercept)
        {
            Float elementZ = LensFrontZ() * -1;

            // Transform _ray_ from camera to lens system space
            Transform LensFromCamera = pbrt.Scale(1, 1, -1);
            Ray ray = LensFromCamera.Apply(r);
            for (int i = 0; i < (int)elementInterfaces.size(); ++i)
            {
                LensElementInterface element = elementInterfaces[(ulong)i];
                bool isStop = (element.curvatureRadius == 0);
                // Compute intersection of ray with lens element
                Float t;
                Normal3f n = default;
                if (isStop)
                    t = -(ray.o.z - elementZ) / ray.d.z;
                else
                {
                    Float radius = element.curvatureRadius;
                    Float zCenter = elementZ + element.curvatureRadius;
                    if (!IntersectSphericalElement(radius, zCenter, ray, &t, &n))
                        return;
                }
                pbrt.CHECK_GE(t, 0.0f);

                printf("Line[{{%f, %f}, {%f, %f}}],", ray.o.z, ray.o.x, ray.Apply(t).z, ray.Apply(t).x);

                // Test intersection point against element aperture
                Point3f pHit = ray.Apply(t);
                Float r2 = pHit.x * pHit.x + pHit.y * pHit.y;
                Float apertureRadius2 = element.apertureRadius * element.apertureRadius;
                if (r2 > apertureRadius2)
                    return;
                ray.o = pHit;

                // Update ray path for from-scene element interface interaction
                if (!isStop)
                {
                    Vector3f wt;
                    Float eta_i = (i == 0 || elementInterfaces[(ulong)(i - 1)].eta == 0.0f)
                                      ? 1.0f
                                      : elementInterfaces[(ulong)(i - 1)].eta;
                    Float eta_t =
                        (elementInterfaces[(ulong)i].eta != 0.0f) ? elementInterfaces[(ulong)i].eta : 1.0f;
                    if (!pbrt.Refract(pbrt.Normalize(-ray.d), n, eta_t / eta_i, null, &wt))
                        return;
                    ray.d = wt;
                }
                elementZ += element.thickness;
            }

            // go to the film plane by default
            {
                Float ta = -ray.o.z / ray.d.z;
                if (toOpticalIntercept)
                {
                    ta = -ray.o.x / ray.d.x;
                    printf("Point[{%f, %f}], ", ray.Apply(ta).z, ray.Apply(ta).x);
                }
                printf("%s[{{%f, %f}, {%f, %f}}]", arrow ? "Arrow" : "Line", ray.o.z, ray.o.x,
                       ray.Apply(ta).z, ray.Apply(ta).x);
            }
        }

        public void RenderExitPupil(Float sx, Float sy, string filename)
        {
            Point3f pFilm = new Point3f(sx, sy, 0);

            const int nSamples = 2048;
            Image image = new Image(PixelFormat.Float, new Point2i(nSamples, nSamples), new string[] { "Y" });

            for (int y = 0; y < nSamples; ++y)
            {
                Float fy = (Float)y / (Float)(nSamples - 1);
                Float ly = pbrt.Lerp(fy, -RearElementRadius(), RearElementRadius());
                for (int x = 0; x < nSamples; ++x)
                {
                    Float fx = (Float)x / (Float)(nSamples - 1);
                    Float lx = pbrt.Lerp(fx, -RearElementRadius(), RearElementRadius());

                    Point3f pRear = new Point3f(lx, ly, LensRearZ());

                    if (lx * lx + ly * ly > RearElementRadius() * RearElementRadius())
                        image.SetChannel(new Point2i(x, y), 0, 1.0f);
                    else if (TraceLensesFromFilm(new Ray(pFilm, pRear - pFilm), null) != 0)
                        image.SetChannel(new Point2i(x, y), 0, 0.5f);
                    else
                        image.SetChannel(new Point2i(x, y), 0, 0.0f);
                }
            }

            image.Write(filename);
        }

        public void TestExitPupilBounds()
        {
            Float filmDiagonal = film.Diagonal();

            Float u = testExitPupilBoundsRng.Uniform<Float>();
            Point3f pFilm = new Point3f(u * filmDiagonal / 2, 0, 0);

            Float r = pFilm.x / (filmDiagonal / 2);
            int pupilIndex = std.min((int)exitPupilBounds.size() - 1,
                                     (int)std.floor(r * (exitPupilBounds.size() - 1)));
            Bounds2f pupilBounds = exitPupilBounds[(ulong)pupilIndex];
            if (pupilIndex + 1 < (int)exitPupilBounds.size())
                pupilBounds = pbrt.Union(pupilBounds, exitPupilBounds[(ulong)(pupilIndex + 1)]);

            // Now, randomly pick points on the aperture and see if any are outside
            // of pupil bounds...
            for (int i = 0; i < 1000; ++i)
            {
                // Braced-init-list: left-to-right.
                Float ux = testExitPupilBoundsRng.Uniform<Float>();
                Float uy = testExitPupilBoundsRng.Uniform<Float>();
                Point2f u2 = new Point2f(ux, uy);
                Point2f pd = pbrt.SampleUniformDiskConcentric(u2);
                pd *= RearElementRadius();

                Ray testRay = new Ray(pFilm, new Point3f(pd.x, pd.y, 0.0f) - pFilm);
                Ray testOut;
                if (TraceLensesFromFilm(testRay, &testOut) == 0)
                    continue;

                if (!pbrt.Inside(pd, pupilBounds))
                {
                    Console.Error.Write(pbrt.StringPrintf("Aha! (%f,%f) went through, but outside bounds (%f,%f) - " +
                                                          "(%f,%f)\n",
                                                          pd.x, pd.y, pupilBounds.pMin[0], pupilBounds.pMin[1],
                                                          pupilBounds.pMax[0], pupilBounds.pMax[1]));
                    RenderExitPupil(
                        (Float)pupilIndex / exitPupilBounds.size() * filmDiagonal / 2.0f, 0.0f,
                        "low.exr");
                    RenderExitPupil(
                        (Float)(pupilIndex + 1) / exitPupilBounds.size() * filmDiagonal / 2.0f,
                        0.0f, "high.exr");
                    RenderExitPupil(pFilm.x, 0.0f, "mid.exr");
                    Environment.Exit(0);
                }
            }
            Console.Error.Write(".");
        }

        // (function-local "static RNG rng" in TestExitPupilBounds)
        static RNG testExitPupilBoundsRng = new RNG();

        static void ComputeCardinalPoints(Ray rIn, Ray rOut, Float* pz, Float* fz)
        {
            Float tf = -rOut.o.x / rOut.d.x;
            *fz = -rOut.Apply(tf).z;
            Float tp = (rIn.o.x - rOut.o.x) / rOut.d.x;
            *pz = -rOut.Apply(tp).z;
        }

        void ComputeThickLensApproximation(Float[] pz, Float[] fz)
        {
            // Find height $x$ from optical axis for parallel rays
            Float x = .001f * film.Diagonal();

            // Compute cardinal points for film side of lens system
            Ray rScene = new Ray(new Point3f(x, 0, LensFrontZ() + 1), new Vector3f(0, 0, -1));
            Ray rFilm;
            if (TraceLensesFromScene(rScene, &rFilm) == 0)
                pbrt.ErrorExit("Unable to trace ray from scene to film for thick lens " +
                                  "approximation. Is aperture stop extremely small?");
            Float p0, f0;
            ComputeCardinalPoints(rScene, rFilm, &p0, &f0);
            pz[0] = p0; fz[0] = f0;

            // Compute cardinal points for scene side of lens system
            rFilm = new Ray(new Point3f(x, 0, LensRearZ() - 1), new Vector3f(0, 0, 1));
            if (TraceLensesFromFilm(rFilm, &rScene) == 0)
                pbrt.ErrorExit("Unable to trace ray from film to scene for thick lens " +
                                  "approximation. Is aperture stop extremely small?");
            Float p1, f1;
            ComputeCardinalPoints(rFilm, rScene, &p1, &f1);
            pz[1] = p1; fz[1] = f1;
        }

        Float FocusThickLens(Float focusDistance)
        {
            Float[] pz = new Float[2], fz = new Float[2];
            ComputeThickLensApproximation(pz, fz);
            pbrt.LOG_VERBOSE("Cardinal points: p' = %f f' = %f, p = %f f = %f.\n", pz[0], fz[0], pz[1], fz[1]);
            pbrt.LOG_VERBOSE("Effective focal length %f\n", fz[0] - pz[0]);
            // Compute translation of lens, _delta_, to focus at _focusDistance_
            Float f = fz[0] - pz[0];
            Float z = -focusDistance;
            Float c = (pz[1] - z - pz[0]) * (pz[1] - z - 4 * f - pz[0]);
            if (c <= 0)
                pbrt.ErrorExit(pbrt.StringPrintf("Coefficient must be positive. It looks focusDistance %f " +
                                                       " is too short for a given lenses configuration",
                                                       focusDistance));
            Float delta = (pz[1] - z + pz[0] - std.sqrt(c)) / 2;

            return elementInterfaces.back().thickness + delta;
        }

        Bounds2f BoundExitPupil(Float filmX0, Float filmX1)
        {
            Bounds2f pupilBounds = new Bounds2f();
            // Sample a collection of points on the rear lens to find exit pupil
            const int nSamples = 1024 * 1024;
            // Compute bounding box of projection of rear element on sampling plane
            Float rearRadius = RearElementRadius();
            Bounds2f projRearBounds = new Bounds2f(new Point2f(-1.5f * rearRadius, -1.5f * rearRadius),
                                                   new Point2f(1.5f * rearRadius, 1.5f * rearRadius));

            for (int i = 0; i < nSamples; ++i)
            {
                // Find location of sample points on $x$ segment and rear lens element
                Point3f pFilm = new Point3f(pbrt.Lerp((i + 0.5f) / nSamples, filmX0, filmX1), 0, 0);
                Float[] u = { pbrt.RadicalInverse(0, (uint64_t)i), pbrt.RadicalInverse(1, (uint64_t)i) };
                Point3f pRear = new Point3f(pbrt.Lerp(u[0], projRearBounds.pMin.x, projRearBounds.pMax.x),
                                            pbrt.Lerp(u[1], projRearBounds.pMin.y, projRearBounds.pMax.y),
                                            LensRearZ());

                // Expand pupil bounds if ray makes it through the lens system
                if (!pbrt.Inside(new Point2f(pRear.x, pRear.y), pupilBounds) &&
                    TraceLensesFromFilm(new Ray(pFilm, pRear - pFilm), null) != 0)
                    pupilBounds = pbrt.Union(pupilBounds, new Point2f(pRear.x, pRear.y));
            }

            // Return degenerate bounds if no rays made it through the lens system
            if (pupilBounds.IsDegenerate())
            {
                pbrt.LOG_VERBOSE("Unable to find exit pupil in x = [%f,%f] on film.", filmX0, filmX1);
                return pupilBounds;
            }

            // Expand bounds to account for sample spacing
            pupilBounds = pbrt.Expand(pupilBounds, 2 * pbrt.Length(projRearBounds.Diagonal()) / std.sqrt((Float)nSamples));

            return pupilBounds;
        }

        pstd.optional<ExitPupilSample> SampleExitPupil(Point2f pFilm, Point2f uLens)
        {
            // Find exit pupil bound for sample distance from film center
            Float rFilm = std.sqrt(pbrt.Sqr(pFilm.x) + pbrt.Sqr(pFilm.y));
            int rIndex = (int)(rFilm / (film.Diagonal() / 2) * exitPupilBounds.size());
            rIndex = std.min((int)exitPupilBounds.size() - 1, rIndex);
            Bounds2f pupilBounds = exitPupilBounds[(ulong)rIndex];
            if (pupilBounds.IsDegenerate())
                return default;

            // Generate sample point inside exit pupil bound
            Point2f pLens = pupilBounds.Lerp(uLens);
            Float pdf = 1 / pupilBounds.Area();

            // Return sample point rotated by angle of _pFilm_ with $+x$ axis
            Float sinTheta = (rFilm != 0) ? pFilm.y / rFilm : 0;
            Float cosTheta = (rFilm != 0) ? pFilm.x / rFilm : 1;
            Point3f pPupil = new Point3f(cosTheta * pLens.x - sinTheta * pLens.y,
                                         sinTheta * pLens.x + cosTheta * pLens.y, LensRearZ());
            return new ExitPupilSample { pPupil = pPupil, pdf = pdf };
        }

        public override pstd.optional<CameraRay> GenerateRay(CameraSample sample, ref SampledWavelengths lambda)
        {
            // Find point on film, _pFilm_, corresponding to _sample.pFilm_
            Point2f s = new Point2f(sample.pFilm.x / film.FullResolution().x, sample.pFilm.y / film.FullResolution().y);
            Point2f pFilm2 = physicalExtent.Lerp(s);
            Point3f pFilm = new Point3f(-pFilm2.x, pFilm2.y, 0);

            // Trace ray from _pFilm_ through lens system
            pstd.optional<ExitPupilSample> eps = SampleExitPupil(new Point2f(pFilm.x, pFilm.y), sample.pLens);
            if (!eps)
                return default;
            Ray rFilm = new Ray(pFilm, eps.value().pPupil - pFilm);
            Ray ray;
            Float weight = TraceLensesFromFilm(rFilm, &ray);
            if (weight == 0)
                return default;

            // Finish initialization of _RealisticCamera_ ray
            ray.time = SampleTime(sample.time);
            ray.medium = medium;
            ray = RenderFromCamera(ray);
            ray.d = pbrt.Normalize(ray.d);

            // Compute weighting for _RealisticCamera_ ray
            Float cosTheta = pbrt.Normalize(rFilm.d).z;
            weight *= pbrt.Pow(4, cosTheta) / (eps.value().pdf * pbrt.Sqr(LensRearZ()));

            return new CameraRay(ray, new SampledSpectrum(weight));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ RealisticCamera %s elementInterfaces: %s exitPupilBounds: %s ]",
                                        base.ToString(), elementInterfaces, exitPupilBounds);
        }

        public static RealisticCamera Create(ParameterDictionary parameters, CameraTransform cameraTransform, Film film,
                                             Medium medium, FileLoc loc, Allocator alloc = null)
        {
            CameraBaseParameters cameraBaseParameters = new CameraBaseParameters(cameraTransform, film, medium,
                                                                                 parameters, loc);

            // Realistic camera-specific parameters
            string lensFile = pbrt.ResolveFilename(parameters.GetOneString("lensfile", ""));
            Float apertureDiameter = parameters.GetOneFloat("aperturediameter", 1.0f);
            Float focusDistance = parameters.GetOneFloat("focusdistance", 10.0f);

            if (lensFile.Length == 0)
            {
                pbrt.Error(loc, "No lens description file supplied!");
                return null;
            }
            // Load element data from lens description file
            List<Float> lensParameters = pbrt.ReadFloatFile(lensFile);
            if (lensParameters.Count == 0)
            {
                pbrt.Error(loc, pbrt.StringPrintf("Error reading lens specification file \"%s\".", lensFile));
                return null;
            }
            if (lensParameters.Count % 4 != 0)
            {
                pbrt.Error(loc, pbrt.StringPrintf("%s: excess values in lens specification file; " +
                                                        "must be multiple-of-four values, read %d.",
                                                        lensFile, (int)lensParameters.Count));
                return null;
            }

            int builtinRes = 256;
            Func<Point2f[], Image> rasterize = (Point2f[] vert) =>
            {
                Image image = new Image(PixelFormat.Float, new Point2i(builtinRes, builtinRes), new string[] { "Y" }, null, alloc);

                for (int y = 0; y < image.Resolution().y; ++y)
                    for (int x = 0; x < image.Resolution().x; ++x)
                    {
                        Point2f p = new Point2f(-1 + 2 * (x + 0.5f) / image.Resolution().x,
                                                -1 + 2 * (y + 0.5f) / image.Resolution().y);
                        int windingNumber = 0;
                        // Test against edges
                        for (int i = 0; i < vert.Length; ++i)
                        {
                            int i1 = (i + 1) % vert.Length;
                            Float e = (p[0] - vert[i][0]) * (vert[i1][1] - vert[i][1]) -
                                      (p[1] - vert[i][1]) * (vert[i1][0] - vert[i][0]);
                            if (vert[i].y <= p.y)
                            {
                                if (vert[i1].y > p.y && e > 0)
                                    ++windingNumber;
                            }
                            else if (vert[i1].y <= p.y && e < 0)
                                --windingNumber;
                        }

                        image.SetChannel(new Point2i(x, y), 0, windingNumber == 0 ? 0.0f : 1.0f);
                    }

                return image;
            };

            string apertureName = parameters.GetOneString("aperture", "");
            Image apertureImage = new Image(alloc);
            if (apertureName.Length != 0)
            {
                // built-in diaphragm shapes
                if (apertureName == "gaussian")
                {
                    apertureImage = new Image(PixelFormat.Float, new Point2i(builtinRes, builtinRes), new string[] { "Y" },
                                              null, alloc);
                    for (int y = 0; y < apertureImage.Resolution().y; ++y)
                        for (int x = 0; x < apertureImage.Resolution().x; ++x)
                        {
                            Point2f uv = new Point2f(-1 + 2 * (x + 0.5f) / apertureImage.Resolution().x,
                                                     -1 + 2 * (y + 0.5f) / apertureImage.Resolution().y);
                            Float r2 = pbrt.Sqr(uv.x) + pbrt.Sqr(uv.y);
                            Float sigma2 = 1;
                            Float v = std.max<Float>(0, std.exp(-r2 / sigma2) - std.exp(-1 / sigma2));
                            apertureImage.SetChannel(new Point2i(x, y), 0, v);
                        }
                }
                else if (apertureName == "square")
                {
                    apertureImage = new Image(PixelFormat.Float, new Point2i(builtinRes, builtinRes), new string[] { "Y" },
                                              null, alloc);
                    for (int y = (int)(.25 * builtinRes); y < (int)(.75 * builtinRes); ++y)
                        for (int x = (int)(.25 * builtinRes); x < (int)(.75 * builtinRes); ++x)
                            apertureImage.SetChannel(new Point2i(x, y), 0, 4.0f);
                }
                else if (apertureName == "pentagon")
                {
                    // https://mathworld.wolfram.com/RegularPentagon.html
                    Float c1 = (std.sqrt(5.0f) - 1) / 4;
                    Float c2 = (std.sqrt(5.0f) + 1) / 4;
                    Float s1 = std.sqrt(10.0f + 2.0f * std.sqrt(5.0f)) / 4;
                    Float s2 = std.sqrt(10.0f - 2.0f * std.sqrt(5.0f)) / 4;
                    // Vertices in CW order.
                    Point2f[] vert = { new Point2f(0, 1), new Point2f(s1, c1), new Point2f(s2, -c2), new Point2f(-s2, -c2),
                                       new Point2f(-s1, c1) };
                    // Scale down slightly
                    for (int i = 0; i < 5; ++i)
                        vert[i] *= .8f;
                    apertureImage = rasterize(vert);
                }
                else if (apertureName == "star")
                {
                    // 5-sided. Vertices are two pentagons--inner and outer radius
                    Point2f[] vert = new Point2f[10];
                    for (int i = 0; i < 10; ++i)
                    {
                        // inner radius: https://math.stackexchange.com/a/2136996
                        Float r = ((i & 1) != 0) ? 1.0f : (std.cos(pbrt.Radians(72.0f)) / std.cos(pbrt.Radians(36.0f)));
                        vert[i] = new Point2f(r * std.cos(Pi * i / 5.0f), r * std.sin(Pi * i / 5.0f));
                    }
                    Array.Reverse(vert);
                    apertureImage = rasterize(vert);
                }
                else
                {
                    ImageAndMetadata im = Image.Read(pbrt.ResolveFilename(apertureName), alloc);
                    apertureImage = im.image;
                    if (apertureImage.NChannels() > 1)
                    {
                        ImageChannelDesc rgbDesc = apertureImage.GetChannelDesc(new string[] { "R", "G", "B" });
                        if (!rgbDesc)
                            pbrt.ErrorExit(pbrt.StringPrintf("%s: didn't find R, G, B channels to average for " +
                                                                   "aperture image.", apertureName));

                        Image mono = new Image(PixelFormat.Float, apertureImage.Resolution(), new string[] { "Y" }, null, alloc);
                        for (int y = 0; y < mono.Resolution().y; ++y)
                            for (int x = 0; x < mono.Resolution().x; ++x)
                            {
                                Float avg = apertureImage.GetChannels(new Point2i(x, y), rgbDesc).Average();
                                mono.SetChannel(new Point2i(x, y), 0, avg);
                            }

                        apertureImage = mono;
                    }
                }

                if (apertureImage)
                {
                    apertureImage.FlipY();

                    // Normalize it so that brightness matches a circular aperture
                    Float sum = 0;
                    for (int y = 0; y < apertureImage.Resolution().y; ++y)
                        for (int x = 0; x < apertureImage.Resolution().x; ++x)
                            sum += apertureImage.GetChannel(new Point2i(x, y), 0);
                    Float avg = sum / (apertureImage.Resolution().x * apertureImage.Resolution().y);

                    Float scale = (Pi / 4) / avg;
                    for (int y = 0; y < apertureImage.Resolution().y; ++y)
                        for (int x = 0; x < apertureImage.Resolution().x; ++x)
                            apertureImage.SetChannel(new Point2i(x, y), 0,
                                                     apertureImage.GetChannel(new Point2i(x, y), 0) * scale);
                }
            }

            return new RealisticCamera(cameraBaseParameters, lensParameters, focusDistance, apertureDiameter,
                                       apertureImage, alloc);
        }

        // RealisticCamera Private Members
        Bounds2f physicalExtent;
        pstd.vector<LensElementInterface> elementInterfaces;
        Image apertureImage;
        pstd.vector<Bounds2f> exitPupilBounds;
    }

    // Camera factory
    public abstract unsafe partial class Camera
    {
        public static Camera Create(string name, ParameterDictionary parameters, Medium medium,
                                    CameraTransform cameraTransform, Film film, FileLoc loc, Allocator alloc)
        {
            Camera camera = null;
            if (name == "perspective")
                camera = PerspectiveCamera.Create(parameters, cameraTransform, film, medium, loc, alloc);
            else if (name == "orthographic")
                camera = OrthographicCamera.Create(parameters, cameraTransform, film, medium, loc, alloc);
            else if (name == "realistic")
                camera = RealisticCamera.Create(parameters, cameraTransform, film, medium, loc, alloc);
            else if (name == "spherical")
                camera = SphericalCamera.Create(parameters, cameraTransform, film, medium, loc, alloc);
            else
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: camera type unknown.", name));

            if (camera == null)
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unable to create camera.", name));

            parameters.ReportUnused();
            return camera;
        }
    }

}  // namespace pbrt
