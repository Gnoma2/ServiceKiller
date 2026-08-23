using System;
using System.Threading;

namespace ServiceKillerV1.Core
{
    // Serializa operaciones elevadas de ServiceKiller para que dos workers no puedan
    // modificar simultáneamente el mismo journal o restaurar/aplicar a la vez.
    internal sealed class MachineOperationLock : IDisposable
    {
        private readonly Mutex _mutex;
        private bool _acquired;

        private MachineOperationLock(Mutex mutex, bool acquired)
        {
            _mutex = mutex;
            _acquired = acquired;
        }

        public static MachineOperationLock Acquire(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds <= 0) timeoutMilliseconds = 30000;
            Mutex mutex = new Mutex(false, @"Global\ServiceKiller-MachineOperation-v1");
            bool acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(timeoutMilliseconds); }
                catch (AbandonedMutexException) { acquired = true; }

                if (!acquired)
                    throw new TimeoutException("Otra operación de ServiceKiller sigue en curso. Espera unos segundos y vuelve a intentarlo.");

                return new MachineOperationLock(mutex, true);
            }
            catch
            {
                if (!acquired) mutex.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_acquired)
            {
                try { _mutex.ReleaseMutex(); } catch { }
                _acquired = false;
            }
            _mutex.Dispose();
        }
    }
}
