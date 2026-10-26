// PtexIO.h
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

// Ptex file format structures and constants.

using System;
using System.Runtime.InteropServices;

namespace Ptex
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Header
    {
        public uint magic;
        public uint version;
        public uint meshtype;
        public uint datatype;
        public int alphachan;
        public ushort nchannels;
        public ushort nlevels;
        public uint nfaces;
        public uint extheadersize;
        public uint faceinfosize;
        public uint constdatasize;
        public uint levelinfosize;
        public uint minorversion;
        public ulong leveldatasize;
        public uint metadatazipsize;
        public uint metadatamemsize;

        public readonly int pixelSize() { return PtexGlobals.DataSize((DataType)datatype) * nchannels; }
        public readonly bool hasAlpha() { return alphachan >= 0 && alphachan < nchannels; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ExtHeader
    {
        public ushort ubordermode;
        public ushort pad;
        public ushort vbordermode;
        public ushort edgefiltermode;
        public uint lmdheaderzipsize;
        public uint lmdheadermemsize;
        public ulong lmddatasize;
        public ulong editdatasize;
        public ulong editdatapos;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LevelInfo
    {
        public ulong leveldatasize;
        public uint levelheadersize;
        public uint nfaces;
    }

    public enum Encoding { enc_constant, enc_zipped, enc_diffzipped, enc_tiled }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FaceDataHeader
    {
        public uint data;  // bits 0..29 = blocksize, bits 30..31 = encoding

        public readonly uint blocksize() { return data & 0x3fffffff; }
        public readonly Encoding encoding() { return (Encoding)((data >> 30) & 0x3); }
        public uint val { readonly get { return data; } set { data = value; } }
        public void set(uint blocksizeArg, Encoding encodingArg)
        {
            data = (blocksizeArg & 0x3fffffff) | (((uint)encodingArg & 0x3) << 30);
        }
    }

    public enum EditType { et_editfacedata, et_editmetadata }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EditFaceDataHeader
    {
        public uint faceid;
        public FaceInfo faceinfo;
        public FaceDataHeader fdh;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EditMetaDataHeader
    {
        public uint metadatazipsize;
        public uint metadatamemsize;
    }

    public static unsafe class PtexIO
    {
        public const uint Magic = 'P' | ('t' << 8) | ('e' << 16) | ((uint)'x' << 24);
        public static readonly int HeaderSize = sizeof(Header);
        public static readonly int ExtHeaderSize = sizeof(ExtHeader);
        public static readonly int LevelInfoSize = sizeof(LevelInfo);
        public static readonly int FaceDataHeaderSize = sizeof(FaceDataHeader);
        public static readonly int EditFaceDataHeaderSize = sizeof(EditFaceDataHeader);
        public static readonly int EditMetaDataHeaderSize = sizeof(EditMetaDataHeader);

        // these constants can be tuned for performance
        public const int IBuffSize = 8192;          // default input buffer size
        public const int BlockSize = 16384;         // target block size for file I/O
        public const int TileSize = 65536;          // target tile size (uncompressed)
        public const int AllocaMax = 16384;         // max size for using alloca
        public const int MetaDataThreshold = 1024;  // cutoff for large meta data

        public static bool LittleEndian()
        {
            return BitConverter.IsLittleEndian;
        }
    }
}
