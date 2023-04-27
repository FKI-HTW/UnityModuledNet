using System;
using UnityEngine;

namespace CENTIS.UnityModuledNet
{
    internal class SyncRuntimeManager : MonoBehaviour
    {
        public event Action OnAwake;
        public event Action OnStart;
        public event Action OnUpdate;

        private readonly static object _lock = new();
        private static SyncRuntimeManager _instance;
        public static SyncRuntimeManager Instance
		{
            get
			{
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = (SyncRuntimeManager)FindObjectOfType(typeof(SyncRuntimeManager));

                        if (_instance == null)
                        {
                            GameObject singletonObject = new();
                            _instance = singletonObject.AddComponent<SyncRuntimeManager>();
                            singletonObject.name = typeof(SyncRuntimeManager).ToString();
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
                SyncManager.Init();
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
