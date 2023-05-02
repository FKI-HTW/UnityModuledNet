using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CENTIS.UnityModuledNet.Modules
{
    public class SyncClientVisualiserSettings : ScriptableObject
    {
        public int ClientVisualiserDelay = 500;
        public SyncClientVisualiser ClientVisualiser;
    }
}
