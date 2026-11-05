// A stand-in for the RuntimeOptimizer (runtimeoptimize.cpp), which another part of the port provides. It has the constructor the shading system
// calls and the members optimize_group reads, and does no optimization, so that ShadingSystem runs end to end at optimization level 0. The optimizer's
// file defines the same partial class: when it lands, delete this file.

using System;
using System.Collections.Generic;
using OIIO;
using OSL;

namespace OSL.pvt
{
    public partial class RuntimeOptimizer : OSOProcessorBase
    {
        public bool m_unknown_textures_needed;
        public SortedSet<ustring> m_textures_needed = new SortedSet<ustring>();
        public bool m_unknown_closures_needed;
        public SortedSet<ustring> m_closures_needed = new SortedSet<ustring>();
        public SortedSet<ustring> m_globals_needed = new SortedSet<ustring>();
        public int m_globals_read;
        public int m_globals_write;
        public List<UserDataNeeded> m_userdata_needed = new List<UserDataNeeded>();
        public bool m_unknown_attributes_needed;
        public SortedSet<AttributeNeeded> m_attributes_needed = new SortedSet<AttributeNeeded>();
        public double m_stat_opt_locking_time;
        public double m_stat_specialization_time;

        public RuntimeOptimizer(ShadingSystemImpl shadingsys, ShaderGroup group, ShadingContext context)
            : base(shadingsys, group, context)
        {
        }

        public override void run()
        {
        }

        public void police_failed_optimizations()
        {
        }

        public string serialize()
        {
            return group().serialize();
        }
    }
}
