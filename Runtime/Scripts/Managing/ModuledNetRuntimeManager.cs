using System;
using UnityEngine;

namespace CENTIS.UnityModuledNet.Managing
{
    internal class ModuledNetRuntimeManager : MonoBehaviour
    {
        public event Action OnAwake;
        public event Action OnStart;
        public event Action OnUpdate;

        private readonly static object _lock = new();
        private static ModuledNetRuntimeManager _instance;
        public static ModuledNetRuntimeManager Instance
		{
            get
			{
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = (ModuledNetRuntimeManager)FindObjectOfType(typeof(ModuledNetRuntimeManager));

                        if (_instance == null)
                        {
                            GameObject singletonObject = new();
                            _instance = singletonObject.AddComponent<ModuledNetRuntimeManager>();
                            singletonObject.name = typeof(ModuledNetRuntimeManager).ToString();
                            DontDestroyOnLoad(singletonObject);
                        }
                    }

                    return _instance;
                }
            }
		}

        [RuntimeInitializeOnLoadMethod]
        private static void InitializeManager()
		{
            if (!Application.isEditor)
			{
                _ = Instance;
                ModuledNetManager.Init();
			}
		}

        private void Awake()
        {
            OnAwake?.Invoke();
        }

        private void Start()
        {
            OnStart?.Invoke();
        }

        private void Update()
        {
            OnUpdate?.Invoke();
        }
    }
}
