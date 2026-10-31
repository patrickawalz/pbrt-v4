// (not in pbrt-v4) boundarystack_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class BoundaryStackTests
    {
        static Medium MakeMedium(Float sigma)
        {
            ConstantSpectrum sigmaSpectrum = new ConstantSpectrum(sigma);
            ConstantSpectrum zero = new ConstantSpectrum(0.0f);
            return new HomogeneousMedium(sigmaSpectrum, sigmaSpectrum, 1.0f, zero, 0.0f, 0.0f, null);
        }

        [TEST("BoundaryStack", "PriorityOrder")]
        public static void BoundaryStack_PriorityOrder()
        {
            Medium air = MakeMedium(0.1f), glass = MakeMedium(0.2f), water = MakeMedium(0.3f);
            BoundaryStack stack = default;
            EXPECT_TRUE(stack.IsEmpty());

            // The scene's medium is below everything else
            stack.Push(new BoundaryStackElement(null, air), BoundaryStack.globalMediumPriority);
            // A high priority element pushed first stays on top of a later one with a lower priority
            stack.Push(new BoundaryStackElement(null, glass), 3);
            stack.Push(new BoundaryStackElement(null, water), 1);

            EXPECT_EQ(stack.Size(), 3);
            EXPECT_TRUE(ReferenceEquals(stack.Top().medium, glass));
            EXPECT_EQ(stack.TopPriority(), 3);
            EXPECT_TRUE(ReferenceEquals(stack.SecondFromTop().medium, water));

            // Leaving the glass exposes the water, then the air
            EXPECT_TRUE(stack.Pop(new BoundaryStackElement(null, glass), 3));
            EXPECT_TRUE(ReferenceEquals(stack.Top().medium, water));
            stack.PopTop();
            EXPECT_TRUE(ReferenceEquals(stack.Top().medium, air));
            EXPECT_EQ(stack.TopPriority(), BoundaryStack.globalMediumPriority);
        }

        [TEST("BoundaryStack", "EqualPriorityLastPushedOnTop")]
        public static void BoundaryStack_EqualPriorityLastPushedOnTop()
        {
            Medium a = MakeMedium(1.0f), b = MakeMedium(2.0f), c = MakeMedium(3.0f);
            BoundaryStack stack = default;
            stack.Push(new BoundaryStackElement(null, a), 2);
            stack.Push(new BoundaryStackElement(null, b), 2);
            stack.Push(new BoundaryStackElement(null, c), 1);

            EXPECT_TRUE(ReferenceEquals(stack.Top().medium, b));
            EXPECT_TRUE(ReferenceEquals(stack.SecondFromTop().medium, a));

            // Removing an element from the middle keeps the order of the others
            EXPECT_TRUE(stack.Pop(new BoundaryStackElement(null, a), 2));
            EXPECT_EQ(stack.Size(), 2);
            EXPECT_TRUE(ReferenceEquals(stack.Top().medium, b));
            EXPECT_TRUE(ReferenceEquals(stack.SecondFromTop().medium, c));
        }

        [TEST("BoundaryStack", "PopMissingDoesNothing")]
        public static void BoundaryStack_PopMissingDoesNothing()
        {
            Medium a = MakeMedium(1.0f), b = MakeMedium(2.0f);
            BoundaryStack stack = default;
            stack.Push(new BoundaryStackElement(null, a), 2);

            // The right element with the wrong priority, and the wrong element with the right priority
            EXPECT_FALSE(stack.Pop(new BoundaryStackElement(null, a), 3));
            EXPECT_FALSE(stack.Pop(new BoundaryStackElement(null, b), 2));
            EXPECT_EQ(stack.Size(), 1);
            EXPECT_TRUE(stack.Contains(new BoundaryStackElement(null, a), 2));
            EXPECT_FALSE(stack.Contains(new BoundaryStackElement(null, b), 2));
        }

        [TEST("BoundaryStack", "CopyIsIndependent")]
        public static void BoundaryStack_CopyIsIndependent()
        {
            // A shadow ray starts with a copy of the stack of the path it was spawned from
            Medium a = MakeMedium(1.0f), b = MakeMedium(2.0f);
            BoundaryStack stack = default;
            stack.Push(new BoundaryStackElement(null, a), 0);
            BoundaryStack copy = stack;
            copy.Push(new BoundaryStackElement(null, b), 1);

            EXPECT_EQ(stack.Size(), 1);
            EXPECT_EQ(copy.Size(), 2);
            EXPECT_TRUE(ReferenceEquals(stack.Top().medium, a));
            EXPECT_TRUE(ReferenceEquals(copy.Top().medium, b));
        }

        [TEST("BoundaryStack", "NestedVolumes")]
        public static void BoundaryStack_NestedVolumes()
        {
            // The ray of figure 13 in Fong et al.: an outer volume V_O around an inner volume V_I, crossing V_I at the
            // intervals [p1, p3] and leaving V_I again. The inner volume has the higher priority.
            Medium outer = MakeMedium(1.0f), inner = MakeMedium(2.0f);
            BoundaryStack stack = default;
            stack.Push(new BoundaryStackElement(null, outer), 1);       // p0: enter V_O
            EXPECT_TRUE(ReferenceEquals(stack.Top().medium, outer));
            stack.Push(new BoundaryStackElement(null, inner), 2);       // p1: enter V_I
            EXPECT_TRUE(ReferenceEquals(stack.Top().medium, inner));
            EXPECT_TRUE(stack.Pop(new BoundaryStackElement(null, inner), 2));   // p3: leave V_I
            EXPECT_TRUE(ReferenceEquals(stack.Top().medium, outer));
            EXPECT_TRUE(stack.Pop(new BoundaryStackElement(null, outer), 1));   // p5: leave V_O
            EXPECT_TRUE(stack.IsEmpty());
        }

        [TEST("BoundaryStack", "InterleavedEntryAndExit")]
        public static void BoundaryStack_InterleavedEntryAndExit()
        {
            // Overlapping, not nested: the ray enters A, enters B, leaves A, leaves B. The medium on top has to follow
            // the priorities and not the order of the events.
            Medium a = MakeMedium(1.0f), b = MakeMedium(2.0f);
            BoundaryStack stack = default;
            stack.Push(new BoundaryStackElement(null, a), 5);
            stack.Push(new BoundaryStackElement(null, b), 3);
            EXPECT_TRUE(ReferenceEquals(stack.Top().medium, a));
            EXPECT_TRUE(stack.Pop(new BoundaryStackElement(null, a), 5));
            EXPECT_TRUE(ReferenceEquals(stack.Top().medium, b));
            EXPECT_TRUE(stack.Pop(new BoundaryStackElement(null, b), 3));
            EXPECT_TRUE(stack.IsEmpty());
        }

        static Interaction MakeInterface(Medium inside, Medium outside)
        {
            Interaction intr = new Interaction();
            intr.n = new Normal3f(0, 0, 1);
            intr.mediumInterface = new MediumInterface(inside, outside);
            return intr;
        }

        [TEST("MediumTracking", "EnterAndLeave")]
        public static void MediumTracking_EnterAndLeave()
        {
            Medium outer = MakeMedium(1.0f), inner = MakeMedium(2.0f);
            // A sphere interface with inner inside and outer outside; the normal points outwards
            Interaction intr = MakeInterface(inner, outer);
            BoundaryStack stack = MediumTracking.Start(outer);

            // Coming from outside (wo along +n) and going in (wi along -n)
            intr.wo = new Vector3f(0, 0, 1);
            MediumTracking.CrossSurface(ref stack, intr, new Vector3f(0, 0, -1));
            EXPECT_EQ(stack.Size(), 1);
            EXPECT_TRUE(ReferenceEquals(MediumTracking.Current(stack), inner));

            // A reflection changes nothing
            intr.wo = new Vector3f(0, 0, -1);
            MediumTracking.CrossSurface(ref stack, intr, new Vector3f(0, 0, -1));
            EXPECT_TRUE(ReferenceEquals(MediumTracking.Current(stack), inner));

            // Coming from inside and going out
            MediumTracking.CrossSurface(ref stack, intr, new Vector3f(0, 0, 1));
            EXPECT_EQ(stack.Size(), 1);
            EXPECT_TRUE(ReferenceEquals(MediumTracking.Current(stack), outer));
        }

        [TEST("MediumTracking", "MissedEntryDoesNotDuplicate")]
        public static void MediumTracking_MissedEntryDoesNotDuplicate()
        {
            // A collision sampled on the boundary leaves the path in the inner medium with the outer one on the stack. Leaving
            // the inner medium must give the outer medium once, as pbrt-v4 does.
            Medium outer = MakeMedium(1.0f), inner = MakeMedium(2.0f);
            Interaction intr = MakeInterface(inner, outer);
            BoundaryStack stack = MediumTracking.Start(outer);
            intr.wo = new Vector3f(0, 0, -1);
            MediumTracking.CrossSurface(ref stack, intr, new Vector3f(0, 0, 1));
            EXPECT_EQ(stack.Size(), 1);
            EXPECT_TRUE(ReferenceEquals(MediumTracking.Current(stack), outer));

            // Leaving the outer medium (a sphere around it) then empties the stack
            Interaction outerShell = MakeInterface(outer, null);
            outerShell.wo = new Vector3f(0, 0, -1);
            MediumTracking.CrossSurface(ref stack, outerShell, new Vector3f(0, 0, 1));
            EXPECT_TRUE(stack.IsEmpty());
        }

        static Interaction MakeVolume(Medium medium, int priority)
        {
            Interaction intr = new Interaction();
            intr.n = new Normal3f(0, 0, 1);
            intr.mediumInterface = new MediumInterface(medium, null, priority);
            return intr;
        }

        [TEST("MediumTracking", "OverlappingVolumesFollowPriority")]
        public static void MediumTracking_OverlappingVolumesFollowPriority()
        {
            // Two overlapping volumes entered and left in an interleaved order: the medium on top follows the priorities
            Medium a = MakeMedium(1.0f), b = MakeMedium(2.0f);
            Interaction volumeA = MakeVolume(a, 2);
            Interaction volumeB = MakeVolume(b, 1);
            BoundaryStack stack = default;
            Vector3f into = new Vector3f(0, 0, -1), outOf = new Vector3f(0, 0, 1);

            volumeA.wo = new Vector3f(0, 0, 1);
            volumeB.wo = new Vector3f(0, 0, 1);
            MediumTracking.CrossSurface(ref stack, volumeA, into);
            MediumTracking.CrossSurface(ref stack, volumeB, into);
            EXPECT_EQ(stack.Size(), 2);
            EXPECT_TRUE(ReferenceEquals(MediumTracking.Current(stack), a));

            // Leaving A exposes B, leaving B empties the stack
            volumeA.wo = new Vector3f(0, 0, -1);
            volumeB.wo = new Vector3f(0, 0, -1);
            MediumTracking.CrossSurface(ref stack, volumeA, outOf);
            EXPECT_TRUE(ReferenceEquals(MediumTracking.Current(stack), b));
            MediumTracking.CrossSurface(ref stack, volumeB, outOf);
            EXPECT_TRUE(stack.IsEmpty());
        }

        [TEST("PhaseBxDF", "MatchesPhaseFunction")]
        public static void PhaseBxDF_MatchesPhaseFunction()
        {
            // The BSDF of a medium point is the phase function: same value, same density, same samples, and no shading frame
            HGPhaseFunction phase = new HGPhaseFunction(0.6f);
            BSDF bsdf = new BSDF(phase);
            RNG rng = new RNG();
            for (int i = 0; i < 100; ++i)
            {
                Vector3f wo = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                Point2f u = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                pstd.optional<PhaseFunctionSample> ps = phase.Sample_p(wo, u);
                pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, 0.5f, u);
                EXPECT_TRUE((bool)ps);
                EXPECT_TRUE((bool)bs);
                EXPECT_EQ(bs.value().pdf, ps.value().pdf);
                EXPECT_EQ(bs.value().f[0], ps.value().p);
                EXPECT_EQ(bs.value().wi.x, ps.value().wi.x);
                EXPECT_EQ(bsdf.f(wo, ps.value().wi)[0], phase.p(wo, ps.value().wi));
                EXPECT_EQ(bsdf.PDF(wo, ps.value().wi), phase.PDF(wo, ps.value().wi));
            }
            // A direction in the xy plane is a valid direction in a medium
            Vector3f inPlane = new Vector3f(0, 1, 0);
            EXPECT_TRUE(bsdf.f(inPlane, new Vector3f(1, 0, 0))[0] > 0);
        }

        [TEST("BoundaryStack", "RandomAgainstReference")]
        public static void BoundaryStack_RandomAgainstReference()
        {
            // Random pushes and pops are compared with a plain list sorted the way the stack is documented to be
            Medium[] media = new Medium[6];
            for (int i = 0; i < media.Length; ++i)
                media[i] = MakeMedium(1.0f + i);
            RNG rng = new RNG();
            for (int trial = 0; trial < 200; ++trial)
            {
                BoundaryStack stack = default;
                System.Collections.Generic.List<(int medium, int priority)> reference =
                    new System.Collections.Generic.List<(int medium, int priority)>();
                for (int step = 0; step < 60; ++step)
                {
                    int medium = (int)(rng.Uniform<Float>() * media.Length);
                    int priority = (int)(rng.Uniform<Float>() * 4);
                    bool push = rng.Uniform<Float>() < 0.55f && reference.Count < BoundaryStack.capacity - 1;
                    if (push)
                    {
                        stack.Push(new BoundaryStackElement(null, media[medium]), priority);
                        int index = reference.Count;
                        while (index > 0 && reference[index - 1].priority > priority)
                            --index;
                        reference.Insert(index, (medium, priority));
                    }
                    else
                    {
                        bool popped = stack.Pop(new BoundaryStackElement(null, media[medium]), priority);
                        int found = -1;
                        for (int i = reference.Count - 1; i >= 0; --i)
                        {
                            if (reference[i].medium == medium && reference[i].priority == priority)
                            {
                                found = i;
                                break;
                            }
                        }
                        EXPECT_EQ(popped, found >= 0);
                        if (found >= 0)
                            reference.RemoveAt(found);
                    }
                    EXPECT_EQ(stack.Size(), reference.Count);
                    if (reference.Count > 0)
                    {
                        EXPECT_TRUE(ReferenceEquals(stack.Top().medium, media[reference[reference.Count - 1].medium]));
                        EXPECT_EQ(stack.TopPriority(), reference[reference.Count - 1].priority);
                    }
                }
            }
        }
    }
}
