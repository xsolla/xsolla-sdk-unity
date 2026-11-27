using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Xsolla.SDK.Utils
{
    internal sealed class RunOnStartThread : MonoBehaviour {
        private readonly Thread startThread = Thread.CurrentThread;
        private List<Action> actions = new List<Action>();

        private static RunOnStartThread _instance;
        private static RunOnStartThread Instance() {
            if (_instance != null) 
                return _instance;
            var obj = new GameObject("Xsolla.SDK.Common.RunOnStartThread");
            obj.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            if (Application.isPlaying)
                DontDestroyOnLoad(obj);
            _instance = obj.AddComponent<RunOnStartThread>();

            return _instance;
        }
        
        public static void Create() {
            Instance();
        }
        
        public static void Run(Action action) {
            var inst = Instance();
            inst.RunAction(action);
        }
        
        private void Awake() {
            DontDestroyOnLoad(gameObject);
        }

        private void Update() {
            List<Action> actionsToRun = null;
            
            lock (actions) {
                if (actions.Count > 0) {
                    actionsToRun = actions;
                    actions = new List<Action>();
                }
            }

            if (actionsToRun == null) return;
            foreach (var action in actionsToRun) {
                try {
                    action();
                }
                catch (Exception exception) {
                    // One failing action must not cost the others their delivery. Reported straight to Unity,
                    // whatever the SDK log level, as it would be had it escaped: it is most likely the app's own.
                    Debug.LogException(exception);
                }
            }
        }
        
        public void RunAction(Action action) {
            if (startThread == Thread.CurrentThread) {
                action();
            }
            else {
                lock (actions) {
                    actions.Add(action);
                }
            }
        }
    }
}
