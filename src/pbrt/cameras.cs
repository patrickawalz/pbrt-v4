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

        public Point3f RenderFromCamera(Point3f p, Float time) { return renderFromCamera.Apply(p, time); }

        public Vector3f RenderFromCamera(Vector3f v, Float time) { return renderFromCamera.Apply(v, time); }

        public Normal3f RenderFromCamera(Normal3f n, Float time) { return renderFromCamera.Apply(n, time); }

        public Ray RenderFromCamera(Ray r) { return renderFromCamera.Apply(r); }

        public RayDifferential RenderFromCamera(RayDifferential r) { return renderFromCamera.Apply(r); }

        public AnimatedTransform RenderFromCamera() { return renderFromCamera; }
        public Point3f CameraFromRender(Point3f p, Float time) { return renderFromCamera.ApplyInverse(p, time); }
        public Transform CameraFromRender(Float time) { return pbrt.Inverse(renderFromCamera.Interpolate(time)); }

        public Vector3f CameraFromRender(Vector3f v, Float time) { return renderFromCamera.ApplyInverse(v, time); }

        public Normal3f CameraFromRender(Normal3f v, Float time) { return renderFromCamera.ApplyInverse(v, time); }
        public Point3f RenderFromWorld(Point3f p) { return worldFromRender.ApplyInverse(p); }

        public Transform RenderFromWorld() { return pbrt.Inverse(worldFromRender); }
        public Transform CameraFromWorld(Float time)
        {
            return pbrt.Inverse(worldFromRender * renderFromCamera.Interpolate(time));
        }

        public bool CameraFromRenderHasScale() { return renderFromCamera.HasScale(); }
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
        public override Medium GetMedium() { return medium; }
        public override CameraTransform GetCameraTransform() { return cameraTransform; }

        public override Float SampleTime(Float u) { return pbrt.Lerp(u, shutterOpen, shutterClose); }

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

        protected Ray RenderFromCamera(Ray r) { return cameraTransform.RenderFromCamera(r); }

        protected RayDifferential RenderFromCamera(RayDifferential r) { return cameraTransform.RenderFromCamera(r); }

        // CameraBase Protected Members
        protected CameraTransform cameraTransform;
        protected Float shutterOpen, shutterClose;
        protected Film film;
        protected Medium medium;
        protected Vector3f minPosDifferentialX, minPosDifferentialY;
        protected Vector3f minDirDifferentialX, minDirDifferentialY;

        protected Vector3f RenderFromCamera(Vector3f v, Float time) { return cameraTransform.RenderFromCamera(v, time); }

        protected Normal3f RenderFromCamera(Normal3f v, Float time) { return cameraTransform.RenderFromCamera(v, time); }

        protected Point3f RenderFromCamera(Point3f p, Float time) { return cameraTransform.RenderFromCamera(p, time); }

        protected Vector3f CameraFromRender(Vector3f v, Float time) { return cameraTransform.CameraFromRender(v, time); }

        protected Normal3f CameraFromRender(Normal3f v, Float time) { return cameraTransform.CameraFromRender(v, time); }

        protected Point3f CameraFromRender(Point3f p, Float time) { return cameraTransform.CameraFromRender(p, time); }

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
    }

    // ProjectiveCamera Definition
    public abstract unsafe class ProjectiveCamera : CameraBase
    {
        // ProjectiveCamera Public Methods
        protected ProjectiveCamera() { }

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

        // OrthographicCamera Method Definitions
        public override pstd.optional<CameraRay> GenerateRay(CameraSample sample, ref SampledWavelengths lambda)
        {
            // Compute raster and camera sample positions
            Point3f pFilm = new Point3f(sample.pFilm.x, sample.pFilm.y, 0);
            Point3f pCamera = cameraFromRaster.Apply(pFilm);

            Ray ray = new Ray(pCamera, new Vector3f(0, 0, 1), SampleTime(sample.time));
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

            RayDifferential ray = new RayDifferential(pCamera, new Vector3f(0, 0, 1), SampleTime(sample.time));
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

            Ray ray = new Ray(new Point3f(0, 0, 0), pbrt.Normalize((Vector3f)pCamera), SampleTime(sample.time));
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
            RayDifferential ray = new RayDifferential(new Point3f(0, 0, 0), dir, SampleTime(sample.time));
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
            return new SampledSpectrum(1 / (A * lensArea * pbrt.Pow4(cosTheta)));
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
            *pdfDir = 1 / (A * pbrt.Pow3(cosTheta));
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

            Ray ray = new Ray(new Point3f(0, 0, 0), dir, SampleTime(sample.time));
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
    public sealed unsafe partial class RealisticCamera : CameraBase
    {
        // RealisticCamera Private Declarations
        struct LensElementInterface
        {
            public Float curvatureRadius;
            public Float thickness;
            public Float eta;
            public Float apertureRadius;
            // (not in pbrt-v4) The conic constant and the coefficients A4, A6, ..., A16 of the aspheric terms of the surface, in
            // millimetres like the lens file; null for a sphere
            public double[] aspheric;
            // (not in pbrt-v4) The Abbe number of the glass behind the interface, 0 for a glass without dispersion
            public Float abbeNumber;
            // (not in pbrt-v4) The Sellmeier coefficients B1, B2, B3, C1, C2, C3 (C in micrometres squared) of the glass behind the
            // interface, null when the glass has none
            public double[] sellmeier;
            // (not in pbrt-v4) The Cauchy coefficients A, B, C (micrometres) of the glass behind the interface, null when it has none
            public double[] cauchy;
            // (not in pbrt-v4) The index of the Sellmeier or Cauchy relation at the d line; the relation is offset to the index of
            // the lens file there
            public double glassIndexD;

            public override string ToString()
            {
                return pbrt.StringPrintf("[ LensElementInterface curvatureRadius: %f thickness: %f " +
                                            "eta: %f apertureRadius: %f ]",
                                            curvatureRadius, thickness, eta, apertureRadius);
            }
        }

        // (not in pbrt-v4) The number of values per interface of the aspheric coefficients parameter: the conic constant k and
        // A4, A6, A8, A10, A12, A14, A16 of the sag
        //   z(r) = c r^2 / (1 + sqrt(1 - (1 + k) c^2 r^2)) + A4 r^4 + A6 r^6 + ... + A16 r^16
        // where c is the curvature of the interface (1 / the radius of the lens file) and r and z are in millimetres. All zero
        // is a sphere.
        const int AsphericValuesPerInterface = 8;

        // (not in pbrt-v4) The sag z(r) of an interface and its slope dz/dr (the radius r and the sag in metres, the slope has no
        // unit), NaN where the conic term is not defined
        static double ElementSag(LensElementInterface element, double r, out double slope)
        {
            double rMm = r * 1000;
            double curvature = 1 / (element.curvatureRadius * 1000.0);
            double conic = element.aspheric != null ? element.aspheric[0] : 0;
            double radicand = 1 - (1 + conic) * curvature * curvature * rMm * rMm;
            if (radicand <= 0)
            {
                slope = double.NaN;
                return double.NaN;
            }

            double root = Math.Sqrt(radicand);
            double sagMm = curvature * rMm * rMm / (1 + root);
            slope = curvature * rMm / root;
            if (element.aspheric != null)
            {
                for (int term = 0; term < 7; ++term)
                {
                    int power = 4 + 2 * term;
                    double coefficient = element.aspheric[1 + term];
                    sagMm += coefficient * Math.Pow(rMm, power);
                    slope += power * coefficient * Math.Pow(rMm, power - 1);
                }
            }

            return sagMm / 1000;
        }

        // (not in pbrt-v4) The wavelengths of the lines F, C and d in micrometres
        const double WavelengthF = 0.4861327;
        const double WavelengthC = 0.6562725;
        const double WavelengthD = 0.5875618;

        // (not in pbrt-v4) The number of values per interface of the Sellmeier and Cauchy coefficients parameters
        const int SellmeierValuesPerInterface = 6;
        const int CauchyValuesPerInterface = 3;

        // (not in pbrt-v4) Sellmeier: n^2 = 1 + sum_i B_i l^2 / (l^2 - C_i), the wavelength l in micrometres
        public static double SellmeierIndex(double[] coefficients, double lambda)
        {
            double lambda2 = lambda * lambda;
            double n2 = 1;
            for (int i = 0; i < 3; ++i)
            {
                n2 += coefficients[i] * lambda2 / (lambda2 - coefficients[3 + i]);
            }

            return n2 > 0 ? Math.Sqrt(n2) : 0;
        }

        // (not in pbrt-v4) Cauchy: n = A + B / l^2 + C / l^4, the wavelength l in micrometres
        public static double CauchyIndex(double[] coefficients, double lambda)
        {
            double lambda2 = lambda * lambda;
            return coefficients[0] + coefficients[1] / lambda2 + coefficients[2] / (lambda2 * lambda2);
        }

        // (not in pbrt-v4) The Sellmeier coefficients of a catalogue glass by name (ignoring case, spaces, hyphens and underscores),
        // null for an unknown name
        public static double[] CatalogueGlass(string name)
        {
            string key = name.ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
            switch (key)
            {
                case "BK7":
                case "NBK7":
                {
                    return new double[] { 1.03961212, 0.231792344, 1.01046945, 0.00600069867, 0.0200179144, 103.560653 };
                }
                case "SF11":
                {
                    return new double[] { 1.73759695, 0.313747346, 1.89878101, 0.013188707, 0.0623068142, 155.23629 };
                }
                case "FUSEDSILICA":
                case "SILICA":
                {
                    return new double[] { 0.6961663, 0.4079426, 0.8974794, 0.00467914825849, 0.013512063073959999, 97.93400253792099 };
                }
                default:
                {
                    return null;
                }
            }
        }

        // (not in pbrt-v4) The index of refraction of the medium behind an interface at a wavelength in nanometres (0 is the
        // d line, which is the index of the lens file). With Sellmeier or Cauchy coefficients the dispersion is that relation,
        // offset so that the d line keeps the index of the lens file: n = n_file + n_model(lambda) - n_model(lambda_d).
        // Otherwise it is the Cauchy relation n = n_d + B (1 / lambda^2 - 1 / lambda_d^2), with the B that gives
        // n_F - n_C = (n_d - 1) / V_d for the Abbe number V_d.
        static Float ElementEta(LensElementInterface element, Float wavelength)
        {
            if (element.eta == 0 || wavelength <= 0)
            {
                return element.eta;
            }

            if (element.sellmeier != null)
            {
                return (Float)(element.eta + SellmeierIndex(element.sellmeier, wavelength * 1e-3) - element.glassIndexD);
            }

            if (element.cauchy != null)
            {
                return (Float)(element.eta + CauchyIndex(element.cauchy, wavelength * 1e-3) - element.glassIndexD);
            }

            if (element.abbeNumber == 0)
            {
                return element.eta;
            }

            double b = ((element.eta - 1.0) / element.abbeNumber) / (1 / (WavelengthF * WavelengthF) - 1 / (WavelengthC * WavelengthC));
            double lambda = wavelength * 1e-3;
            return (Float)(element.eta + b * (1 / (lambda * lambda) - 1 / (WavelengthD * WavelengthD)));
        }

        public override pstd.optional<CameraRayDifferential> GenerateRayDifferential(CameraSample sample,
                                                                                     ref SampledWavelengths lambda)
        {
            return CameraBase.GenerateRayDifferential(this, sample, ref lambda);
        }

        // (not in pbrt-v4) Importance, densities and connections of the realistic camera for bidirectional methods. A camera
        // vertex is a point on the front element of the lens system, with the normal of that surface and the area measure of
        // it. The importance of a ray that leaves the lens system is
        //   We = T / A_film
        // for the rays that GenerateRay() can produce, and zero for the others: the radiance theorem (the etendue
        // dA cos(theta) dw of the film is that of the exit ray in the air on both sides of a lens system) turns the weight of
        // GenerateRay(), T cos^4 / (pdf_pupil z^2), and its density on the film and the pupil into a constant. The joint density
        // of the position and the direction of the exit ray, in the surface area and the solid angle, is
        //   p = pdf_pupil z^2 |n . w_exit| / (A_film cos^4(theta_film)).
        // Bidirectional path tracing needs the density of a direction given the position, and SampleWi() chooses the position
        // uniformly on the part of the front element within its aperture, so that is what pdfPos is, and pdfDir is p / pdfPos:
        // the strategies that sample the position (t = 1) and those that sample the ray (t > 1) are then compared through the
        // one density p, and the weights of the strategies are right.
        bool FindFilmSample(Ray ray, Float wavelength, out Point2f pRaster, out Float pdfPupil, out Float cosFilm, out Float weight)
        {
            pRaster = default;
            pdfPupil = 0;
            cosFilm = 0;
            weight = 0;
            // The light goes along -ray.d through the optics, from the front element to the film
            Vector3f dCamera = pbrt.Normalize(CameraFromRender(-ray.d, ray.time));
            if (dCamera.z >= 0)
            {
                return false;
            }

            Point3f oCamera = CameraFromRender(ray.o, ray.time);
            Float back = 1e-3f * std.max(1, LensFrontZ());
            Ray rScene = new Ray(oCamera - back * dCamera, dCamera);
            Ray rFilm;
            if (TraceLensesFromScene(rScene, &rFilm, null, wavelength) == 0 || rFilm.d.z >= 0)
            {
                return false;
            }

            Float tFilm = -rFilm.o.z / rFilm.d.z;
            Point3f pFilm = rFilm.o + tFilm * rFilm.d;
            Point2f pFilm2 = new Point2f(-pFilm.x, pFilm.y);
            if (!pbrt.Inside(pFilm2, physicalExtent))
            {
                return false;
            }

            Vector2f s = physicalExtent.Offset(pFilm2);
            pRaster = new Point2f(s.x * film.FullResolution().x, s.y * film.FullResolution().y);

            // The pupil sample that gives the ray: the film to pupil direction is the reverse of the light direction
            Vector3f dFilm = -pbrt.Normalize(rFilm.d);
            cosFilm = dFilm.z;
            Point3f pPupil = pFilm + dFilm * (LensRearZ() / dFilm.z);
            Float rFilmRadius = std.sqrt(pbrt.Sqr(pFilm.x) + pbrt.Sqr(pFilm.y));
            int rIndex = (int)(rFilmRadius / (film.Diagonal() / 2) * exitPupilBounds.size());
            rIndex = std.min((int)exitPupilBounds.size() - 1, rIndex);
            Bounds2f pupilBounds = exitPupilBounds[(ulong)rIndex];
            if (pupilBounds.IsDegenerate())
            {
                return false;
            }

            Float sinTheta = (rFilmRadius != 0) ? pFilm.y / rFilmRadius : 0;
            Float cosTheta = (rFilmRadius != 0) ? pFilm.x / rFilmRadius : 1;
            Point2f pLens = new Point2f(cosTheta * pPupil.x + sinTheta * pPupil.y, -sinTheta * pPupil.x + cosTheta * pPupil.y);
            if (!pbrt.Inside(pLens, pupilBounds))
            {
                return false;
            }

            pdfPupil = 1 / pupilBounds.Area();
            // The transmission of the aperture image: what GenerateRay() gets from tracing this sample out
            weight = TraceLensesFromFilm(new Ray(pFilm, pPupil - pFilm), null, wavelength);
            return weight != 0;
        }

        // The front element is sampled uniformly by area within its aperture. The area within a radius r is
        //   A(r) = integral of 2 pi r' sqrt(1 + sag'(r')^2) dr',
        // tabulated against r^2, where the integrand is smooth, and inverted by interpolation.
        // (not in pbrt-v4) Whether the index of refraction of some element depends on the wavelength. The rays of a dispersive
        // camera have the first wavelength of the sample and the others are terminated, as for the dispersion of a dielectric.
        bool dispersive;

        // The wavelengths that the density of the rays tries, and the wavelengths that bound the exit pupil: the nominal one
        // (0) and the ends of the visible range
        static readonly Float[] nominalWavelengths = { 0 };
        static readonly Float[] dispersiveWavelengths = { 0, 400, 700 };
        Float[] ProbeWavelengths()
        {
            return dispersive ? dispersiveWavelengths : nominalWavelengths;
        }

        const int FrontAreaTableSize = 2048;
        double[] frontAreaTable;
        Float frontArea;

        void ComputeFrontAreaTable()
        {
            LensElementInterface front = elementInterfaces[0];
            double radiusSquared = (double)front.apertureRadius * front.apertureRadius;
            frontAreaTable = new double[FrontAreaTableSize + 1];
            double previousIntegrand = 0;
            for (int i = 0; i <= FrontAreaTableSize; ++i)
            {
                // dA / d(r^2) = pi sqrt(1 + sag'^2)
                double r = Math.Sqrt(radiusSquared * i / FrontAreaTableSize);
                ElementSag(front, r, out double slope);
                double integrand = Math.PI * Math.Sqrt(1 + slope * slope);
                if (i > 0)
                {
                    frontAreaTable[i] = frontAreaTable[i - 1] +
                                        0.5 * (previousIntegrand + integrand) * radiusSquared / FrontAreaTableSize;
                }

                previousIntegrand = integrand;
            }

            frontArea = (Float)frontAreaTable[FrontAreaTableSize];
        }

        Float FrontElementRadius()
        {
            return elementInterfaces[0].apertureRadius;
        }

        // The area of the part of the front element within its aperture
        Float FrontElementArea()
        {
            return frontArea;
        }

        // A point of the front element, uniformly by area, in camera space
        Point3f SampleFrontElement(Point2f u)
        {
            double target = u.x * frontAreaTable[FrontAreaTableSize];
            int low = 0;
            int high = FrontAreaTableSize;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (frontAreaTable[middle] <= target)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            double fraction = (target - frontAreaTable[low]) / (frontAreaTable[low + 1] - frontAreaTable[low]);
            double radiusSquared = (double)FrontElementRadius() * FrontElementRadius();
            double r = Math.Sqrt(radiusSquared * (low + fraction) / FrontAreaTableSize);
            double phi = 2 * Math.PI * u.y;
            double sag = ElementSag(elementInterfaces[0], r, out _);
            // The vertex is at -LensFrontZ in lens space; camera space has the z axis the other way round
            return new Point3f((Float)(r * Math.Cos(phi)), (Float)(r * Math.Sin(phi)), (Float)(LensFrontZ() - sag));
        }

        // The normal of the front element, towards the scene, at a point of it (in camera space)
        Normal3f FrontElementNormal(Point3f pCamera)
        {
            Float r = std.sqrt(pbrt.Sqr(pCamera.x) + pbrt.Sqr(pCamera.y));
            ElementSag(elementInterfaces[0], r, out double slope);
            Float radial = r > 0 ? (Float)(slope / r) : 0;
            return (Normal3f)pbrt.Normalize(new Vector3f(radial * pCamera.x, radial * pCamera.y, 1));
        }

        public override SampledSpectrum We(Ray ray, ref SampledWavelengths lambda, Point2f* pRaster2 = null)
        {
            Float wavelength = 0;
            if (dispersive)
            {
                lambda.TerminateSecondary();
                wavelength = lambda[0];
            }

            if (!FindFilmSample(ray, wavelength, out Point2f pRaster, out _, out _, out Float weight))
            {
                return new SampledSpectrum(0.0f);
            }

            if (pRaster2 != null)
            {
                *pRaster2 = pRaster;
            }

            if (!pbrt.Inside(pRaster, film.SampleBounds()))
            {
                return new SampledSpectrum(0.0f);
            }

            return new SampledSpectrum(weight / physicalExtent.Area());
        }

        public override void PDF_We(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            *pdfPos = *pdfDir = 0;
            // The density of the ray that GenerateRay() would sample: it does not depend on the wavelength, except where the
            // ray is within the aperture for some wavelengths only, and that is found with the first wavelength that passes
            Point2f pRaster = default;
            Float pdfPupil = 0;
            Float cosFilm = 0;
            bool found = false;
            Float[] wavelengths = ProbeWavelengths();
            for (int i = 0; i < wavelengths.Length && !found; ++i)
            {
                found = FindFilmSample(ray, wavelengths[i], out pRaster, out pdfPupil, out cosFilm, out _);
            }

            if (!found)
            {
                return;
            }

            if (!pbrt.Inside(pRaster, film.SampleBounds()))
            {
                return;
            }

            // The point of the ray on the front element: within its aperture
            Point3f oCamera = CameraFromRender(ray.o, ray.time);
            if (pbrt.Sqr(oCamera.x) + pbrt.Sqr(oCamera.y) > pbrt.Sqr(FrontElementRadius()) * (1 + 1e-4f))
            {
                return;
            }

            Vector3f wCamera = pbrt.Normalize(CameraFromRender(ray.d, ray.time));
            Float cosSurface = pbrt.AbsDot(FrontElementNormal(oCamera), wCamera);
            Float p = pdfPupil * pbrt.Sqr(LensRearZ()) * cosSurface / (physicalExtent.Area() * pbrt.Pow4(cosFilm));
            Float area = FrontElementArea();
            *pdfPos = 1 / area;
            *pdfDir = p * area;
        }

        public override pstd.optional<CameraWiSample> SampleWi(Interaction reference, Point2f u, ref SampledWavelengths lambda)
        {
            // Uniformly sample the front element within its aperture
            Point3f pCamera = SampleFrontElement(u);
            Point3f pLensRender = RenderFromCamera(pCamera, reference.time);
            Normal3f n = (Normal3f)pbrt.Normalize(RenderFromCamera((Vector3f)FrontElementNormal(pCamera), reference.time));
            Interaction lensIntr = new Interaction(pLensRender, n, reference.time, medium);

            // Find incident direction to camera _wi_ at _ref_
            Vector3f wi = lensIntr.p() - reference.p();
            Float dist = pbrt.Length(wi);
            wi /= dist;

            // The density of the point, converted to the solid angle at the reference
            Float pdf = pbrt.Sqr(dist) / (pbrt.AbsDot(lensIntr.n, wi) * FrontElementArea());

            Point2f pRaster;
            SampledSpectrum Wi = We(new Ray(lensIntr.SpawnRay(-wi)), ref lambda, &pRaster);
            if (!Wi)
                return default;
            return new CameraWiSample(Wi, wi, pdf, pRaster, reference, lensIntr);
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

        // (not in pbrt-v4) The intersection of a ray with an aspheric interface whose vertex is at elementZ, by Newton's method on
        //   F(t) = z(t) - elementZ - sag(r(t))
        // in double precision, started at the intersection with the sphere of the vertex curvature
        static bool IntersectAsphericalElement(LensElementInterface element, Float elementZ, Ray ray, Float* t, Normal3f* n)
        {
            double tCurrent;
            Float tSphere;
            Normal3f nSphere = default;
            if (IntersectSphericalElement(element.curvatureRadius, elementZ + element.curvatureRadius, ray, &tSphere, &nSphere))
            {
                tCurrent = tSphere;
            }
            else
            {
                tCurrent = (elementZ - ray.o.z) / ray.d.z;
            }

            bool converged = false;
            double px = 0;
            double py = 0;
            double slope = 0;
            double r = 0;
            for (int iteration = 0; iteration < 32 && !converged; ++iteration)
            {
                px = ray.o.x + tCurrent * ray.d.x;
                py = ray.o.y + tCurrent * ray.d.y;
                double pz = ray.o.z + tCurrent * ray.d.z;
                r = Math.Sqrt(px * px + py * py);
                double sag = ElementSag(element, r, out slope);
                if (double.IsNaN(sag))
                {
                    return false;
                }

                double f = pz - elementZ - sag;
                double fPrime = ray.d.z - (r > 0 ? slope * (px * ray.d.x + py * ray.d.y) / r : 0);
                if (fPrime == 0)
                {
                    return false;
                }

                double step = f / fPrime;
                tCurrent -= step;
                converged = Math.Abs(step) < 1e-12;
            }

            if (!converged || tCurrent < 0)
            {
                return false;
            }

            *t = (Float)tCurrent;
            // The gradient of F: the normal is perpendicular to the surface, facing the ray
            px = ray.o.x + tCurrent * ray.d.x;
            py = ray.o.y + tCurrent * ray.d.y;
            r = Math.Sqrt(px * px + py * py);
            ElementSag(element, r, out slope);
            double radial = r > 0 ? slope / r : 0;
            *n = new Normal3f((Float)(-radial * px), (Float)(-radial * py), 1);
            *n = pbrt.FaceForward(pbrt.Normalize(*n), -ray.d);
            return true;
        }

        // (not in pbrt-v4) The intersection of a ray with the interface of a lens element, spherical or aspheric
        static bool IntersectElement(LensElementInterface element, Float elementZ, Ray ray, Float* t, Normal3f* n)
        {
            if (element.aspheric == null)
            {
                return IntersectSphericalElement(element.curvatureRadius, elementZ + element.curvatureRadius, ray, t, n);
            }

            return IntersectAsphericalElement(element, elementZ, ray, t, n);
        }

        // RealisticCamera Method Definitions
        public RealisticCamera(CameraBaseParameters baseParameters, List<Float> lensParameters, Float focusDistance,
                               Float setApertureDiameter, Image apertureImage, Allocator alloc,
                               List<Float> asphericParameters = null, List<Float> abbeNumbers = null,
                               List<Float> sellmeierParameters = null, List<Float> cauchyParameters = null,
                               List<string> glassNames = null, bool diffraction = false)
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
                // (not in pbrt-v4) The aspheric terms of the interface: all zero is a sphere
                double[] aspheric = null;
                if (asphericParameters != null)
                {
                    int first = i / 4 * AsphericValuesPerInterface;
                    bool any = false;
                    double[] values = new double[AsphericValuesPerInterface];
                    for (int k = 0; k < AsphericValuesPerInterface; ++k)
                    {
                        values[k] = asphericParameters[first + k];
                        any |= values[k] != 0;
                    }

                    aspheric = any ? values : null;
                }

                // (not in pbrt-v4) The glass model of the interface: a catalogue name, else Sellmeier coefficients, else Cauchy
                // coefficients, else the Abbe number. All zero (or an empty name) is no model.
                double[] sellmeier = null;
                double[] cauchy = null;
                if (glassNames != null && glassNames[i / 4].Length > 0 && glassNames[i / 4] != "none")
                {
                    sellmeier = CatalogueGlass(glassNames[i / 4]);
                    if (sellmeier == null)
                    {
                        pbrt.ErrorExit(pbrt.StringPrintf("\"%s\" is not a glass of the catalogue (BK7, SF11, fused silica).",
                                                         glassNames[i / 4]));
                    }
                }

                if (sellmeier == null && sellmeierParameters != null)
                {
                    int first = i / 4 * SellmeierValuesPerInterface;
                    double[] values = new double[SellmeierValuesPerInterface];
                    bool any = false;
                    for (int k = 0; k < SellmeierValuesPerInterface; ++k)
                    {
                        values[k] = sellmeierParameters[first + k];
                        any |= values[k] != 0;
                    }

                    sellmeier = any ? values : null;
                }

                if (sellmeier == null && cauchyParameters != null)
                {
                    int first = i / 4 * CauchyValuesPerInterface;
                    double[] values = new double[CauchyValuesPerInterface];
                    bool any = false;
                    for (int k = 0; k < CauchyValuesPerInterface; ++k)
                    {
                        values[k] = cauchyParameters[first + k];
                        any |= values[k] != 0;
                    }

                    cauchy = any ? values : null;
                }

                // Add element interface to end of _elementInterfaces_
                elementInterfaces.push_back(new LensElementInterface
                {
                    curvatureRadius = curvatureRadius,
                    thickness = thickness,
                    eta = eta,
                    apertureRadius = apertureDiameter / 2,
                    aspheric = aspheric,
                    abbeNumber = abbeNumbers != null ? abbeNumbers[i / 4] : 0,
                    sellmeier = eta > 1 ? sellmeier : null,
                    cauchy = eta > 1 ? cauchy : null,
                    glassIndexD = sellmeier != null ? SellmeierIndex(sellmeier, WavelengthD)
                                                    : cauchy != null ? CauchyIndex(cauchy, WavelengthD) : 0
                });
                dispersive |= eta > 1 && ((abbeNumbers != null && abbeNumbers[i / 4] > 0) || sellmeier != null || cauchy != null);
            }
            ComputeFrontAreaTable();

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

            // (not in pbrt-v4) The point spread functions of diffraction at the pupil
            this.diffraction = diffraction;
            if (diffraction)
            {
                BuildDiffractionTables();
            }

            // Compute minimum differentials for _RealisticCamera_
            FindMinimumDifferentials(this);
        }

        Float TraceLensesFromFilm(Ray rCamera, Ray* rOut, Float wavelength = 0)
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
                    if (!IntersectElement(element, elementZ, rLens, &t, &n))
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
                    Float eta_i = ElementEta(element, wavelength);
                    Float eta_t = (i > 0 && elementInterfaces[(ulong)(i - 1)].eta != 0)
                                      ? ElementEta(elementInterfaces[(ulong)(i - 1)], wavelength)
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

        static void ComputeCardinalPoints(Ray rIn, Ray rOut, Float* pz, Float* fz)
        {
            Float tf = -rOut.o.x / rOut.d.x;
            *fz = -rOut.Apply(tf).z;
            Float tp = (rIn.o.x - rOut.o.x) / rOut.d.x;
            *pz = -rOut.Apply(tp).z;
        }

        // (C's printf/fprintf(stderr, ...) for the debugging output below)
        static void printf(string fmt, params object[] args)
        {
            Console.Out.Write(pbrt.StringPrintf(fmt, args));
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

                // Expand pupil bounds if ray makes it through the lens system (at any of the wavelengths of a dispersive one)
                if (!pbrt.Inside(new Point2f(pRear.x, pRear.y), pupilBounds))
                {
                    Float[] wavelengths = ProbeWavelengths();
                    for (int k = 0; k < wavelengths.Length; ++k)
                    {
                        if (TraceLensesFromFilm(new Ray(pFilm, pRear - pFilm), null, wavelengths[k]) != 0)
                        {
                            pupilBounds = pbrt.Union(pupilBounds, new Point2f(pRear.x, pRear.y));
                            break;
                        }
                    }
                }
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
            // (not in pbrt-v4) A dispersive camera traces the first wavelength of the sample, whether or not the ray gets through
            Float wavelength = 0;
            if (dispersive)
            {
                lambda.TerminateSecondary();
                wavelength = lambda[0];
            }

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
            Float weight = TraceLensesFromFilm(rFilm, &ray, wavelength);
            if (weight == 0)
                return default;

            // Finish initialization of _RealisticCamera_ ray
            ray.time = SampleTime(sample.time);
            ray = RenderFromCamera(ray);
            ray.d = pbrt.Normalize(ray.d);

            // Compute weighting for _RealisticCamera_ ray
            Float cosTheta = pbrt.Normalize(rFilm.d).z;
            weight *= pbrt.Pow4(cosTheta) / (eps.value().pdf * pbrt.Sqr(LensRearZ()));

            return new CameraRay(ray, new SampledSpectrum(weight));
        }

        // (function-local "static RNG rng" in TestExitPupilBounds)
        static RNG testExitPupilBoundsRng = new RNG();

        // (not in pbrt-v4) The Mueller matrix of the lens system for light that goes through it (see PolarisedSensor)
        sealed class LensMueller
        {
            public PolarisedLightSystem.Attenuation attenuation = PolarisedLightSystem.Attenuation.Identity();
        }

        // A lens space vector in render space (lens space has z the other way round than camera space)
        Vector3f RenderFromLens(Vector3f v)
        {
            return RenderFromCamera(new Vector3f(v.x, v.y, -v.z), 0);
        }

        // (not in pbrt-v4) The light that goes through a refracting element interface: the transmission Mueller matrix of the Fresnel
        // equations, in the frames of the plane of incidence of that interface (the x axis is the s axis). The scalar factors of
        // radiance of the interfaces (eta^2 of each) cancel in a lens system that starts and ends in air.
        void RecordTransmission(LensMueller recorder, Vector3f travelIn, Vector3f travelOut, Normal3f n, Float eta)
        {
            Vector3f normal = new Vector3f(n.x, n.y, n.z);
            Float cosTheta = pbrt.Dot(normal, -travelIn);
            pbrt.FrDielectricPolarised(cosTheta, eta, out Float Fs, out Float Fp, out Float Ds, out Float Dp);
            PolarisedLightSystem.Attenuation transmission = PolarisedLightSystem.Attenuation.Fresnel(new SampledSpectrum(1.0f),
                                                                                                    1 - Fs, 1 - Fp, 0, 0);
            transmission = transmission.Scattered(RenderFromLens(travelOut), RenderFromLens(-travelIn), RenderFromLens(normal), true);
            // Later interfaces act after the earlier ones
            recorder.attenuation = transmission * recorder.attenuation;
        }

        public override bool PolarisedSensor(Ray ray, Float wavelength, out PolarisedLightSystem.Attenuation optics,
                                             out Vector3f sensorDirection, out Vector3f sensorUp)
        {
            optics = default;
            sensorDirection = default;
            sensorUp = default;
            // The light goes along -ray.d through the optics, from the front element to the film
            Vector3f dCamera = CameraFromRender(-ray.d, ray.time);
            Point3f oCamera = CameraFromRender(ray.o, ray.time);
            LensMueller recorder = new LensMueller();
            Ray rFilm;
            // Start a little in front of the first element so that the trace meets it
            Float back = 1e-3f * std.max(1, LensFrontZ());
            Ray rScene = new Ray(oCamera - back * pbrt.Normalize(dCamera), pbrt.Normalize(dCamera));
            if (TraceLensesFromScene(rScene, &rFilm, recorder, wavelength) == 0)
                return false;
            optics = recorder.attenuation;
            // The light arrives at the film along the direction of the ray that ends there; the up of the image is -y on the film
            // (the lens turns the image over, and the row 0 of the film is the lowest y)
            sensorDirection = pbrt.Normalize(RenderFromCamera(rFilm.d, ray.time));
            sensorUp = RenderFromCamera(new Vector3f(0, -1, 0), ray.time);
            return true;
        }

        Float TraceLensesFromScene(Ray rCamera, Ray* rOut, LensMueller recorder = null, Float wavelength = 0)
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
                    if (!IntersectElement(element, elementZ, rLens, &t, &n))
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
                                      : ElementEta(elementInterfaces[(ulong)(i - 1)], wavelength);
                    Float eta_t = (elementInterfaces[(ulong)i].eta != 0) ? ElementEta(elementInterfaces[(ulong)i], wavelength) : 1;
                    if (!pbrt.Refract(pbrt.Normalize(-rLens.d), n, eta_t / eta_i, null, &wt))
                        return 0;
                    if (recorder != null)
                    {
                        RecordTransmission(recorder, pbrt.Normalize(rLens.d), pbrt.Normalize(wt), n, eta_t / eta_i);
                    }
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
                    if (!IntersectElement(element, elementZ, ray, &t, &n))
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
                    if (!IntersectElement(element, elementZ, ray, &t, &n))
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

            // (not in pbrt-v4) The aspheric terms of the interfaces, in the order of the lens file
            List<Float> asphericParameters = parameters.GetFloatArray("asphericcoefficients");
            if (asphericParameters.Count == 0)
            {
                asphericParameters = null;
            }
            else if (asphericParameters.Count != lensParameters.Count / 4 * AsphericValuesPerInterface)
            {
                pbrt.Error(loc, pbrt.StringPrintf("\"asphericcoefficients\" needs %d values (the conic constant and A4 to A16) " +
                                                        "for each of the %d interfaces of the lens, read %d.",
                                                        AsphericValuesPerInterface, (int)(lensParameters.Count / 4),
                                                        (int)asphericParameters.Count));
                return null;
            }

            // (not in pbrt-v4) The Abbe numbers of the glasses behind the interfaces, in the order of the lens file: 0 for no dispersion
            List<Float> abbeNumbers = parameters.GetFloatArray("abbenumbers");
            if (abbeNumbers.Count == 0)
            {
                abbeNumbers = null;
            }
            else if (abbeNumbers.Count != lensParameters.Count / 4)
            {
                pbrt.Error(loc, pbrt.StringPrintf("\"abbenumbers\" needs a value for each of the %d interfaces of the lens, read %d.",
                                                        (int)(lensParameters.Count / 4), (int)abbeNumbers.Count));
                return null;
            }

            // (not in pbrt-v4) The glass of the interfaces, in the order of the lens file: a catalogue name per interface ("none" or
            // "" for no glass), or 6 Sellmeier values (B1 B2 B3 C1 C2 C3, C in micrometres squared) or 3 Cauchy values (A B C,
            // micrometres) per interface with all zero for no glass. The name takes precedence over Sellmeier, which takes
            // precedence over Cauchy, which takes precedence over the Abbe number. The index of the lens file stays the index at
            // the d line.
            List<string> glassNames = parameters.GetStringArray("glassnames");
            if (glassNames.Count == 0)
            {
                glassNames = null;
            }
            else if (glassNames.Count != lensParameters.Count / 4)
            {
                pbrt.Error(loc, pbrt.StringPrintf("\"glassnames\" needs a name for each of the %d interfaces of the lens, read %d.",
                                                        (int)(lensParameters.Count / 4), (int)glassNames.Count));
                return null;
            }

            List<Float> sellmeierParameters = parameters.GetFloatArray("sellmeiercoefficients");
            if (sellmeierParameters.Count == 0)
            {
                sellmeierParameters = null;
            }
            else if (sellmeierParameters.Count != lensParameters.Count / 4 * SellmeierValuesPerInterface)
            {
                pbrt.Error(loc, pbrt.StringPrintf("\"sellmeiercoefficients\" needs %d values for each of the %d interfaces of the " +
                                                        "lens, read %d.", SellmeierValuesPerInterface,
                                                        (int)(lensParameters.Count / 4), (int)sellmeierParameters.Count));
                return null;
            }

            List<Float> cauchyParameters = parameters.GetFloatArray("cauchycoefficients");
            if (cauchyParameters.Count == 0)
            {
                cauchyParameters = null;
            }
            else if (cauchyParameters.Count != lensParameters.Count / 4 * CauchyValuesPerInterface)
            {
                pbrt.Error(loc, pbrt.StringPrintf("\"cauchycoefficients\" needs %d values for each of the %d interfaces of the " +
                                                        "lens, read %d.", CauchyValuesPerInterface,
                                                        (int)(lensParameters.Count / 4), (int)cauchyParameters.Count));
                return null;
            }

            // (not in pbrt-v4) "none" (the default) or "aperture": the image is convolved with the point spread function of diffraction
            // at the pupil of each film point (see realisticcamera_diffraction.cs)
            string diffractionMode = parameters.GetOneString("diffraction", "none");
            if (diffractionMode != "none" && diffractionMode != "aperture")
            {
                pbrt.Error(loc, pbrt.StringPrintf("\"%s\": unknown \"diffraction\" (must be \"none\" or \"aperture\").",
                                                        diffractionMode));
                return null;
            }

            bool diffraction = diffractionMode == "aperture";

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
                                       apertureImage, alloc, asphericParameters, abbeNumbers,
                                       sellmeierParameters, cauchyParameters, glassNames, diffraction);
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
