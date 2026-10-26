// taggedptr_test.cpp
//
// NOT PORTED (by design): these tests exercise the TaggedPointer<Ts...> template (type-tagged
// 64-bit pointers with Is<T>(), Cast<T>(), Tag(), DispatchCPU() and TypeIndex<T>()). Magi replaces
// every TaggedPointer-based handle (Shape, Light, Material, Sampler, ...) with an abstract class
// plus sealed subclasses (virtual dispatch and C# type tests), so there is no TaggedPointer type
// to test; the equivalent behaviour is covered by every test and render that goes through those
// handles. util/taggedptr.h itself has no counterpart file for the same reason.
