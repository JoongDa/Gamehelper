namespace CraftHelper
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using GameHelper;
    using GameOffsets.Objects.UiElement;

    // Bind the core's existing read-only memory reader; never open a separate process handle.
    internal sealed class UiMemoryReader
    {
        private delegate bool TryRead<T>(IntPtr address, out T value) where T : unmanaged;

        private static readonly PropertyInfo? HandleProperty = typeof(GameProcess).GetProperty(
            "Handle", BindingFlags.Instance | BindingFlags.NonPublic);
        private object? handle;
        private TryRead<UiElementBaseOffset>? readNode;
        private TryRead<IntPtr>? readPointer;
        private Func<IntPtr, int, IntPtr[]>? readPointers;

        internal bool Attach()
        {
            var current = HandleProperty?.GetValue(Core.Process);
            if (current is not SafeHandle safeHandle || safeHandle.IsClosed || safeHandle.IsInvalid)
            {
                this.handle = null;
                return false;
            }

            if (ReferenceEquals(current, this.handle)) return true;
            var methods = current.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
            var read = methods.Single(m => m.Name == "TryReadMemory" && m.IsGenericMethodDefinition);
            this.readNode = read.MakeGenericMethod(typeof(UiElementBaseOffset))
                .CreateDelegate<TryRead<UiElementBaseOffset>>(current);
            this.readPointer = read.MakeGenericMethod(typeof(IntPtr)).CreateDelegate<TryRead<IntPtr>>(current);
            this.readPointers = methods.Single(m => m.Name == "ReadMemoryArray" && m.IsGenericMethodDefinition)
                .MakeGenericMethod(typeof(IntPtr)).CreateDelegate<Func<IntPtr, int, IntPtr[]>>(current);
            this.handle = current;
            return true;
        }

        internal bool TryReadNode(IntPtr address, out UiElementBaseOffset node)
        {
            node = default;
            return IsAddress(address) && this.readNode != null && this.readNode(address, out node) &&
                   (node.Self == IntPtr.Zero || node.Self == address);
        }

        internal bool TryReadPointer(IntPtr address, out IntPtr pointer)
        {
            pointer = IntPtr.Zero;
            return IsAddress(address) && this.readPointer != null && this.readPointer(address, out pointer);
        }

        internal bool TryReadPointers(IntPtr address, int count, out IntPtr[] pointers)
        {
            pointers = Array.Empty<IntPtr>();
            if (count == 0) return true;
            if (!IsAddress(address) || count < 0 || count > 4096 || this.readPointers == null) return false;
            pointers = this.readPointers(address, count);
            return pointers.Length == count;
        }

        internal static bool IsAddress(IntPtr address) =>
            address.ToInt64() >= 0x10000 && address.ToInt64() <= 0x7FFFFFFFFFFF;
    }
}
