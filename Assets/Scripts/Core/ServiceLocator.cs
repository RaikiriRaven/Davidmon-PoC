using System;
using System.Collections.Generic;
using UnityEngine;

namespace Davidmon.Core
{
    /// <summary>
    /// Lightweight service container. Systems register themselves once (e.g. in Awake)
    /// and consumers resolve them instead of using FindObjectOfType. Services can be
    /// swapped at runtime, which keeps the prototype decoupled from the persistent
    /// backend that replaces it later.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();

        public static void Register<T>(T service) where T : class
        {
            if (service == null) return;
            Services[typeof(T)] = service;
        }

        public static void Unregister<T>() where T : class
        {
            Services.Remove(typeof(T));
        }

        /// <summary>
        /// Resolve a service. Returns null if not registered.
        /// </summary>
        public static T Get<T>() where T : class
        {
            if (Services.TryGetValue(typeof(T), out var service))
                return service as T;
            return null;
        }

        /// <summary>
        /// Resolve a service or create it via the provided factory and register it.
        /// </summary>
        public static T GetOrCreate<T>(Func<T> factory) where T : class
        {
            var service = Get<T>();
            if (service != null) return service;
            service = factory();
            Register(service);
            return service;
        }

        public static void Clear()
        {
            Services.Clear();
        }
    }
}