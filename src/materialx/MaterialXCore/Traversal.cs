// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Traversal.h and
// Traversal.cpp.

using System;
using System.Collections;
using System.Collections.Generic;

namespace MaterialX
{
    // Edge Definition
    public struct Edge : IEquatable<Edge>
    {
        // Edge Public Methods
        public Edge(Element elemDown, Element elemConnect, Element elemUp)
        {
            _elemDown = elemDown;
            _elemConnect = elemConnect;
            _elemUp = elemUp;
        }

        public static bool operator ==(Edge lhs, Edge rhs)
        {
            return ReferenceEquals(lhs._elemDown, rhs._elemDown) &&
                   ReferenceEquals(lhs._elemConnect, rhs._elemConnect) &&
                   ReferenceEquals(lhs._elemUp, rhs._elemUp);
        }

        public static bool operator !=(Edge lhs, Edge rhs)
        {
            return !(lhs == rhs);
        }

        public bool Equals(Edge other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Edge other && this == other;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_elemDown),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_elemConnect),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_elemUp));
        }

        public static implicit operator bool(Edge edge)
        {
            return edge != Mx.getNullEdge();
        }

        public Element getDownstreamElement()
        {
            return _elemDown;
        }

        public Element getConnectingElement()
        {
            return _elemConnect;
        }

        public Element getUpstreamElement()
        {
            return _elemUp;
        }

        public string getName()
        {
            return _elemConnect != null ? _elemConnect.getName() : Mx.EMPTY_STRING;
        }

        // Edge Private Members
        private Element _elemDown;
        private Element _elemConnect;
        private Element _elemUp;
    }

    // TreeIterator Definition
    // operator++ is next(), comparison with end() is isEnd(), and the iterator
    // can be used directly in a foreach statement.
    public class TreeIterator : IEnumerable<Element>
    {
        // TreeIterator Public Methods
        public TreeIterator(Element elem)
        {
            _elem = elem;
            _prune = false;
            _holdCount = 0;
        }

        public bool isEnd()
        {
            return _elem == null && _stack.Count == 0 && !_prune;
        }

        public TreeIterator next()
        {
            if (_holdCount != 0)
            {
                _holdCount--;
                return this;
            }

            if (!_prune && _elem != null && _elem.getChildren().Count != 0)
            {
                // Traverse to the first child of this element.
                _stack.Add(new StackFrame(_elem, 0));
                _elem = _elem.getChildren()[0];
                return this;
            }
            _prune = false;

            while (true)
            {
                if (_stack.Count == 0)
                {
                    // Traversal is complete.
                    _elem = null;
                    return this;
                }

                // Traverse to our siblings.
                StackFrame parentFrame = _stack[_stack.Count - 1];
                List<Element> siblings = parentFrame.first.getChildren();
                if (parentFrame.second + 1 < siblings.Count)
                {
                    _elem = siblings[++parentFrame.second];
                    return this;
                }

                // Traverse to our parent's siblings.
                _stack.RemoveAt(_stack.Count - 1);
            }
        }

        public Element getElement()
        {
            return _elem;
        }

        public int getElementDepth()
        {
            return _stack.Count;
        }

        public void setPruneSubtree(bool prune)
        {
            _prune = prune;
        }

        public bool getPruneSubtree()
        {
            return _prune;
        }

        public TreeIterator begin(int holdCount = 0)
        {
            _holdCount = holdCount;
            return this;
        }

        public IEnumerator<Element> GetEnumerator()
        {
            for (TreeIterator it = begin(); !it.isEnd(); it.next())
            {
                yield return it.getElement();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // TreeIterator Private Types
        private class StackFrame
        {
            public StackFrame(Element first, int second)
            {
                this.first = first;
                this.second = second;
            }

            public Element first;
            public int second;
        }

        // TreeIterator Private Members
        private Element _elem;
        private List<StackFrame> _stack = new List<StackFrame>();
        private bool _prune;
        private int _holdCount;
    }

    // GraphIterator Definition
    public class GraphIterator : IEnumerable<Edge>
    {
        // GraphIterator Public Methods
        public GraphIterator(Element elem)
        {
            _upstreamElem = elem;
            _prune = false;
            _holdCount = 0;
            _pathElems.Add(elem);
        }

        public bool isEnd()
        {
            return _upstreamElem == null && _stack.Count == 0 && !_prune;
        }

        public Edge getEdge()
        {
            return new Edge(getDownstreamElement(),
                            getConnectingElement(),
                            getUpstreamElement());
        }

        public GraphIterator next()
        {
            if (_holdCount != 0)
            {
                _holdCount--;
                return this;
            }

            if (!_prune && _upstreamElem != null && _upstreamElem.getUpstreamEdgeCount() != 0)
            {
                // Traverse to the first upstream edge of this element.
                _stack.Add(new StackFrame(_upstreamElem, 0));
                Edge nextEdge = _upstreamElem.getUpstreamEdge(0);
                if (nextEdge && nextEdge.getUpstreamElement() != null && !skipOrMarkAsVisited(nextEdge))
                {
                    extendPathUpstream(nextEdge.getUpstreamElement(), nextEdge.getConnectingElement());
                    return this;
                }
            }
            _prune = false;

            while (true)
            {
                if (_upstreamElem != null)
                {
                    returnPathDownstream(_upstreamElem);
                }

                if (_stack.Count == 0)
                {
                    // Traversal is complete.
                    _upstreamElem = null;
                    _connectingElem = null;
                    _pathElems.Clear();
                    _pathElems.Add(null);
                    _visitedEdges.Clear();
                    _prune = false;
                    _holdCount = 0;
                    return this;
                }

                // Traverse to our siblings.
                StackFrame parentFrame = _stack[_stack.Count - 1];
                if (parentFrame.second + 1 < parentFrame.first.getUpstreamEdgeCount())
                {
                    Edge nextEdge = parentFrame.first.getUpstreamEdge(++parentFrame.second);
                    if (nextEdge && nextEdge.getUpstreamElement() != null && !skipOrMarkAsVisited(nextEdge))
                    {
                        extendPathUpstream(nextEdge.getUpstreamElement(), nextEdge.getConnectingElement());
                        return this;
                    }
                    continue;
                }

                // Traverse to our parent's siblings.
                returnPathDownstream(parentFrame.first);
                _stack.RemoveAt(_stack.Count - 1);
            }
        }

        public Element getDownstreamElement()
        {
            return _stack.Count != 0 ? _stack[_stack.Count - 1].first : null;
        }

        public Element getConnectingElement()
        {
            return _connectingElem;
        }

        public Element getUpstreamElement()
        {
            return _upstreamElem;
        }

        public int getUpstreamIndex()
        {
            return _stack.Count != 0 ? _stack[_stack.Count - 1].second : 0;
        }

        public int getElementDepth()
        {
            return _stack.Count;
        }

        public int getNodeDepth()
        {
            int nodeDepth = 0;
            foreach (Element elem in _pathElems)
            {
                if (elem.isA<Node>())
                {
                    nodeDepth++;
                }
            }
            return nodeDepth;
        }

        public void setPruneSubgraph(bool prune)
        {
            _prune = prune;
        }

        public bool getPruneSubgraph()
        {
            return _prune;
        }

        public GraphIterator begin(int holdCount = 0)
        {
            // Increment once to generate a valid edge.
            if (_stack.Count == 0)
            {
                next();
            }

            _holdCount = holdCount;
            return this;
        }

        public IEnumerator<Edge> GetEnumerator()
        {
            for (GraphIterator it = begin(); !it.isEnd(); it.next())
            {
                yield return it.getEdge();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // GraphIterator Private Methods
        private void extendPathUpstream(Element upstreamElem, Element connectingElem)
        {
            // Check for cycles.
            if (_pathElems.Contains(upstreamElem))
            {
                throw new ExceptionFoundCycle("Encountered cycle at element: " + upstreamElem.asString());
            }

            // Extend the current path to the new element.
            _pathElems.Add(upstreamElem);
            _upstreamElem = upstreamElem;
            _connectingElem = connectingElem;
        }

        private void returnPathDownstream(Element upstreamElem)
        {
            _pathElems.Remove(upstreamElem);
            _upstreamElem = null;
            _connectingElem = null;
        }

        private bool skipOrMarkAsVisited(Edge edge)
        {
            bool inserted = _visitedEdges.Add(edge);
            return !inserted;
        }

        // GraphIterator Private Types
        private class StackFrame
        {
            public StackFrame(Element first, int second)
            {
                this.first = first;
                this.second = second;
            }

            public Element first;
            public int second;
        }

        // GraphIterator Private Members
        private Element _upstreamElem;
        private Element _connectingElem;
        private HashSet<Element> _pathElems = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        private List<StackFrame> _stack = new List<StackFrame>();
        private HashSet<Edge> _visitedEdges = new HashSet<Edge>();
        private bool _prune;
        private int _holdCount;
    }

    // InheritanceIterator Definition
    public class InheritanceIterator : IEnumerable<Element>
    {
        // InheritanceIterator Public Methods
        public InheritanceIterator(Element elem)
        {
            _elem = elem;
            _holdCount = 0;
            _pathElems.Add(elem);
        }

        public bool isEnd()
        {
            return _elem == null;
        }

        public Element getElement()
        {
            return _elem;
        }

        public InheritanceIterator next()
        {
            if (_holdCount != 0)
            {
                _holdCount--;
                return this;
            }

            if (_elem != null)
            {
                Element super = _elem.getInheritsFrom();
                if (super != null && super.getCategory() != _elem.getCategory())
                {
                    super = null;
                }
                if (super != null)
                {
                    // Check for cycles.
                    if (_pathElems.Contains(super))
                    {
                        throw new ExceptionFoundCycle("Encountered cycle at element: " + super.asString());
                    }
                    _pathElems.Add(super);
                }
                _elem = super;
            }
            return this;
        }

        public InheritanceIterator begin(int holdCount = 0)
        {
            _holdCount = holdCount;
            return this;
        }

        public IEnumerator<Element> GetEnumerator()
        {
            for (InheritanceIterator it = begin(); !it.isEnd(); it.next())
            {
                yield return it.getElement();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        // InheritanceIterator Private Members
        private Element _elem;
        private HashSet<Element> _pathElems = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        private int _holdCount;
    }

    // ExceptionFoundCycle Definition
    public class ExceptionFoundCycle : Exception
    {
        public ExceptionFoundCycle(string msg) : base(msg)
        {
        }
    }

    // Traversal Functions
    public static partial class Mx
    {
        public static Edge getNullEdge()
        {
            return new Edge(null, null, null);
        }
    }
}
