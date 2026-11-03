//
//   Copyright 2017 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSubdiv.Vtr;

namespace OpenSubdiv.Far
{
    //  SparseMatrix
    //
    //  The SparseMatrix class is used by the PatchBuilder to store coefficients
    //  for a set of patch points derived from some other set of points -- usually
    //  the refined points in a subdivision level.  The compressed sparse row
    //  format (CSR) is used as it provides us with stencils for points that
    //  correspond to rows and so can be more directly and efficiently copied.
    //
    //  It has potential for other uses and so may eventually warrant a seperate
    //  header file of its own.  For now, in keeping with the trend of exposing
    //  classes only where used, it is defined with the PatchBuilder.
    //
    //  We may also want to explore the possibility of being able to assign
    //  static buffers as members here -- allowing common matrices to be set
    //  directly rather than repeatedly replicated.
    //
    public class SparseMatrix<REAL> where REAL : unmanaged
    {
        //  typedef int  column_type;
        //  typedef REAL element_type;

        //  Declaration and access methods:
        public SparseMatrix()
        {
            _numRows = 0;
            _numColumns = 0;
            _numElements = 0;
            _rowOffsets = new StdVector<int>();
            _columns = new StdVector<int>();
            _elements = new StdVector<REAL>();
        }

        public int GetNumRows() { return _numRows; }
        public int GetNumColumns() { return _numColumns; }
        public int GetNumElements() { return _numElements; }

        public int GetCapacity()
        {
            return (int)_elements.size();
        }

        public int GetRowSize(int rowIndex)
        {
            return _rowOffsets[rowIndex + 1] - _rowOffsets[rowIndex];
        }

        public ConstArray<int> GetRowColumns(int rowIndex)
        {
            return new ConstArray<int>(_columns.Data, _rowOffsets[rowIndex],
                                       GetRowSize(rowIndex));
        }
        public ConstArray<REAL> GetRowElements(int rowIndex)
        {
            return new ConstArray<REAL>(_elements.Data, _rowOffsets[rowIndex],
                                        GetRowSize(rowIndex));
        }

        public ConstArray<int> GetColumns()
        {
            return new ConstArray<int>(_columns.Data, 0, GetNumElements());
        }
        public ConstArray<REAL> GetElements()
        {
            return new ConstArray<REAL>(_elements.Data, 0, GetNumElements());
        }

        //  Modification methods
        public void Resize(int numRows, int numCols, int numElementsToReserve)
        {
            _numRows = numRows;
            _numColumns = numCols;
            _numElements = 0;

            _rowOffsets.Resize(0);
            _rowOffsets.Resize(_numRows + 1, -1);
            _rowOffsets[0] = 0;

            if (numElementsToReserve > GetCapacity())
            {
                _columns.Resize(numElementsToReserve);
                _elements.Resize(numElementsToReserve);
            }
        }

        public void Copy(SparseMatrix<REAL> src)
        {
            _numRows = src._numRows;
            _numColumns = src._numColumns;

            _rowOffsets = new StdVector<int>(src._rowOffsets);

            _numElements = src._numElements;

            _columns = new StdVector<int>(src._columns);
            _elements = new StdVector<REAL>(src._elements);
        }

        public void Swap(SparseMatrix<REAL> other)
        {
            int tmpNumRows = _numRows;
            _numRows = other._numRows;
            other._numRows = tmpNumRows;

            int tmpNumColumns = _numColumns;
            _numColumns = other._numColumns;
            other._numColumns = tmpNumColumns;

            int tmpNumElements = _numElements;
            _numElements = other._numElements;
            other._numElements = tmpNumElements;

            _rowOffsets.Swap(other._rowOffsets);
            _columns.Swap(other._columns);
            _elements.Swap(other._elements);
        }

        public void SetRowSize(int rowIndex, int rowSize)
        {
            System.Diagnostics.Debug.Assert(_rowOffsets[rowIndex] == _numElements);

            ref int newVectorSize = ref _rowOffsets[rowIndex + 1];
            newVectorSize = _rowOffsets[rowIndex] + rowSize;

            _numElements = newVectorSize;
            if (newVectorSize > GetCapacity())
            {
                _columns.Resize(newVectorSize);
                _elements.Resize(newVectorSize);
            }
        }

        public Array<int> SetRowColumns(int rowIndex)
        {
            return new Array<int>(_columns.Data, _rowOffsets[rowIndex],
                                  GetRowSize(rowIndex));
        }
        public Array<REAL> SetRowElements(int rowIndex)
        {
            return new Array<REAL>(_elements.Data, _rowOffsets[rowIndex],
                                   GetRowSize(rowIndex));
        }

        //  Simple dimensions:
        private int _numRows;
        private int _numColumns;
        private int _numElements;

        private StdVector<int> _rowOffsets;  // remember one more entry here than rows

        //  XXXX (barfowl) - Note that the use of std::vector for the columns and
        //  element arrays was causing performance issues in the incremental
        //  resizing of consecutive rows, so we've been exploring alternatives...
        private StdVector<int> _columns;
        private StdVector<REAL> _elements;
    }
}
