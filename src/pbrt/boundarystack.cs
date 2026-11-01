// (not in pbrt-v4) boundarystack.cs
//
// The priority stack of PolarisedUPBP / smallUPBP (Path/PriorityStack.hxx StaticPriorityStack2 and Path/BoundaryStack.hxx),
// which replaces the medium that pbrt-v4 carries in every Ray. A path owns one stack: the entries are the boundaries
// (material and medium) the path is currently inside of, ordered by priority, so the entry on top is the one that
// decides the medium of the segment and the index of refraction at the next interface. Fong et al.'s "Production
// Volume Rendering" (section 3.2) calls the same structure the list of materials a ray is inside of.

using System;
using System.Runtime.CompilerServices;

namespace pbrt
{

    // BoundaryStackElement Definition
    public struct BoundaryStackElement
    {
        // BoundaryStackElement Public Methods
        public BoundaryStackElement(Material material, Medium medium)
        {
            this.material = material;
            this.medium = medium;
        }

        // The overlap queries of the material of the entry (Fong et al., section 3.9)
        public BoundaryStackElement(Material material, Medium medium, bool canOverlap, int overlapPriority)
        {
            this.material = material;
            this.medium = medium;
            this.canOverlap = canOverlap;
            this.overlapPriority = overlapPriority;
        }

        public static bool operator ==(BoundaryStackElement a, BoundaryStackElement b)
        {
            // A material that creates its volume for every point of entry is on the stack by the material: the volume that
            // it creates where the path leaves is another one
            return ReferenceEquals(a.material, b.material) && (a.material != null || ReferenceEquals(a.medium, b.medium)) &&
                   a.canOverlap == b.canOverlap && a.overlapPriority == b.overlapPriority;
        }

        public static bool operator !=(BoundaryStackElement a, BoundaryStackElement b)
        {
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            return obj is BoundaryStackElement other && this == other;
        }

        public override int GetHashCode()
        {
            return (RuntimeHelpers.GetHashCode(material) * 31 + (material != null ? 0 : RuntimeHelpers.GetHashCode(medium))) * 31 +
                   (canOverlap ? overlapPriority + 1 : 0);
        }

        // BoundaryStackElement Public Members
        public Material material;
        public Medium medium;
        public bool canOverlap;
        public int overlapPriority;
    }

    [InlineArray(BoundaryStack.capacity)]
    public struct BoundaryStackStorage
    {
        BoundaryStackElement element0;
    }

    [InlineArray(BoundaryStack.capacity)]
    public struct BoundaryStackPriorities
    {
        int priority0;
    }

    // BoundaryStack Definition
    public struct BoundaryStack
    {
        // BoundaryStack Constants
        public const int capacity = 20;

        // The priority of the medium the scene lives in, which every other entry has priority over, and the priority of a
        // thin wall that never changes the medium (as in PolarisedUPBP).
        public const int globalMediumPriority = -2;
        public const int thinWallPriority = -1;

        // BoundaryStack Public Methods
        // The element at the top of the stack
        public BoundaryStackElement Top()
        {
            pbrt.DCHECK_GT(size, 0);
            return elements[size - 1];
        }

        // The priority of the element at the top of the stack
        public int TopPriority()
        {
            pbrt.DCHECK_GT(size, 0);
            return priorities[size - 1];
        }

        // The element below the top of the stack
        public BoundaryStackElement SecondFromTop()
        {
            pbrt.DCHECK_GT(size, 1);
            return elements[size - 2];
        }

        // Inserts the element right below all elements with a higher priority, and above all elements with the same priority
        public void Push(BoundaryStackElement element, int priority)
        {
            if (size == capacity)
            {
                // PolarisedUPBP drops the element on top and goes on
                pbrt.Warning("BoundaryStack overflow: the element on top is discarded.");
                --size;
            }
            int index = size;
            while (index > 0 && priorities[index - 1] > priority)
                --index;
            for (int i = size; i > index; --i)
            {
                elements[i] = elements[i - 1];
                priorities[i] = priorities[i - 1];
            }
            elements[index] = element;
            priorities[index] = priority;
            ++size;
        }

        // Removes the element with the priority, the one nearest the top if it is on the stack more than once. Returns
        // whether it was found.
        public bool Pop(BoundaryStackElement element, int priority)
        {
            for (int i = size - 1; i >= 0 && priorities[i] >= priority; --i)
            {
                if (priorities[i] == priority && elements[i] == element)
                {
                    for (int j = i; j < size - 1; ++j)
                    {
                        elements[j] = elements[j + 1];
                        priorities[j] = priorities[j + 1];
                    }
                    --size;
                    elements[size] = default;
                    return true;
                }
            }
            return false;
        }

        // Whether the element with the priority is on the stack
        public bool Contains(BoundaryStackElement element, int priority)
        {
            for (int i = size - 1; i >= 0 && priorities[i] >= priority; --i)
            {
                if (priorities[i] == priority && elements[i] == element)
                    return true;
            }
            return false;
        }

        // Discards the element on top of the stack
        public void PopTop()
        {
            pbrt.DCHECK_GT(size, 0);
            --size;
            elements[size] = default;
        }

        public void Clear()
        {
            for (int i = 0; i < size; ++i)
                elements[i] = default;
            size = 0;
        }

        // The element that many places below the top (0 is the top)
        public BoundaryStackElement ElementFromTop(int index)
        {
            pbrt.DCHECK_LT(index, size);
            return elements[size - 1 - index];
        }

        public int Size() { return size; }

        public bool IsEmpty() { return size == 0; }

        // BoundaryStack Private Members
        BoundaryStackStorage elements;
        BoundaryStackPriorities priorities;
        int size;
    }

    // MediumTracking Definition
    //
    // The code that VolPathIntegrator and BDPTIntegrator share to know which medium a segment of a path is in, in place of
    // the medium that a Ray carries in pbrt-v4.
    public static class MediumTracking
    {
        // The priority of the entries made from a MediumInterface or from the medium a camera or a light starts in. All
        // of them have the same priority, so the entry made last is on top; priorities arrive with the materials that own a volume.
        public const int interfacePriority = 0;

        // Whether a material of the scene creates its volume for every point of entry (Fong et al., Material::CreateVolume). The
        // volume then depends on where a segment entered, so the strategies that build paths in both directions or reconnect
        // them see different volumes in the same place; only the forward path tracers define it.
        public static bool perEntryVolumes;

        // (not in pbrt-v4) Whether the scene has a RefractiveMedium: the paths in it are curved, which only the walk of volpath
        // follows (docs/refractive-media.md)
        public static bool hasRefractive;

        public static void RequireNoRefractive(string integrator)
        {
            if (hasRefractive)
                pbrt.ErrorExit("The \"" + integrator + "\" integrator does not support refractive media: their light paths are " +
                               "curved, and connections between points of a path are not straight lines. Use \"volpath\".");
        }

        public static void RequireNoPerEntryVolumes(string integrator)
        {
            if (perEntryVolumes)
                pbrt.ErrorExit("The \"" + integrator + "\" integrator does not support materials with a \"volumescale\": their volume " +
                               "depends on the point of entry, which paths that are built or reconnected from the other end " +
                               "do not share. Use \"volpath\".");
        }

        // The stack of a path that starts in the medium (the camera medium, the medium of a light)
        public static BoundaryStack Start(Medium medium)
        {
            BoundaryStack stack = default;
            if (medium != null)
                stack.Push(new BoundaryStackElement(null, medium), interfacePriority);
            return stack;
        }

        // The medium the next segment of the path is in
        // (Fong et al., section 3.9) When the media on top of the stack can overlap, the segment is in all of them at once:
        // their composite, which runs one integrator over the summed coefficients. A medium that cannot overlap hides
        // everything below it, and ends the media that join the composite.
        public static Medium Current(in BoundaryStack stack)
        {
            if (stack.IsEmpty())
                return null;
            BoundaryStackElement top = stack.Top();
            if (top.medium == null || !top.canOverlap)
                return top.medium;
            int count = 1;
            while (count < stack.Size())
            {
                BoundaryStackElement below = stack.ElementFromTop(count);
                if (below.medium == null || !below.canOverlap)
                    break;
                ++count;
            }
            return count == 1 ? top.medium : CompositeMediumCache.Get(stack, count);
        }

        // Updates the stack when a path leaves a surface in direction wi. Only the surfaces with a MediumInterface that is a
        // transition change anything, and only when the path goes through them: it leaves the medium of the side it came from
        // and enters the medium of the side it goes to. On a reflection the sides are the same and nothing changes.
        public static void CrossSurface(ref BoundaryStack stack, Interaction intr, Vector3f wi)
        {
            MediumInterface mediumInterface = intr.mediumInterface;
            if (mediumInterface == null)
                return;
            bool cameFromOutside = pbrt.Dot(intr.wo, intr.n) > 0;
            bool goesToOutside = pbrt.Dot(wi, intr.n) > 0;
            if (cameFromOutside == goesToOutside)
                return;
            if (mediumInterface.HasPriority())
            {
                // A volume boundary: only the medium inside the shape is entered or left, with the priority of the shape
                if (mediumInterface.inside != null)
                {
                    Material owner = mediumInterface.volumeMaterial;
                    BoundaryStackElement element = new BoundaryStackElement(
                        owner, !goesToOutside && owner != null ? owner.GetVolume(intr) : mediumInterface.inside,
                        mediumInterface.canOverlap, mediumInterface.overlapPriority);
                    if (goesToOutside)
                        stack.Pop(element, mediumInterface.priority);
                    else
                        stack.Push(element, mediumInterface.priority);
                }
                return;
            }
            if (!mediumInterface.IsMediumTransition())
                return;
            Medium left = goesToOutside ? mediumInterface.inside : mediumInterface.outside;
            Medium entered = goesToOutside ? mediumInterface.outside : mediumInterface.inside;
            bool popped = false;
            if (left != null)
                popped = stack.Pop(new BoundaryStackElement(null, left), interfacePriority);
            if (entered != null)
            {
                // A path can be inside a medium without having crossed its boundary when a collision is sampled at the very
                // end of a segment (the next ray then starts a rounding error short of the boundary and misses the entry).
                // Leaving that medium finds the medium it enters already on top: nothing is added, as pbrt-v4, which names
                // the medium at every crossing, ends up in that medium too.
                bool alreadyEntered = left != null && !popped && !stack.IsEmpty() &&
                                      stack.Top() == new BoundaryStackElement(null, entered);
                if (!alreadyEntered)
                    stack.Push(new BoundaryStackElement(null, entered), interfacePriority);
            }
        }
    }

}  // namespace pbrt
