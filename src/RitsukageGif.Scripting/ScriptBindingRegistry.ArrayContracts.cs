using System.Runtime.CompilerServices;

namespace RitsukageGif.Scripting
{
    public sealed partial class ScriptBindingRegistry
    {
        [ThreadStatic] private static ValidationTransaction? _validationTransaction;
        [ThreadStatic] private static ValidationTransaction? _spareValidationTransaction;
        private readonly ConditionalWeakTable<List<object?>, ArrayContract> _arrayContracts = new();

        private static ValidationTransaction BeginValidation(out bool ownsTransaction, out int checkpoint)
        {
            ownsTransaction = _validationTransaction is null;
            var transaction = _validationTransaction ??= _spareValidationTransaction ?? new ValidationTransaction();
            if (ownsTransaction) _spareValidationTransaction = null;
            checkpoint = transaction.Pending.Count;
            return transaction;
        }

        private static void EndValidation(ValidationTransaction transaction, bool ownsTransaction, int checkpoint,
            bool success)
        {
            try
            {
                if (!success)
                {
                    transaction.Pending.RemoveRange(checkpoint, transaction.Pending.Count - checkpoint);
                    return;
                }

                if (!ownsTransaction) return;
                foreach (var pending in transaction.Pending)
                {
                    var contract =
                        pending.Registry._arrayContracts.GetValue(pending.Array, static _ => new());
                    lock (contract)
                        if (!contract.ElementTypes.Contains(pending.ElementType, StringComparer.Ordinal))
                            contract.ElementTypes = [.. contract.ElementTypes, pending.ElementType];
                }
            }
            finally
            {
                if (ownsTransaction)
                {
                    transaction.Pending.Clear();
                    _validationTransaction = null;
                    if (transaction.Pending.Capacity <= 256) _spareValidationTransaction = transaction;
                }
            }
        }

        private string[] GetArrayElementContracts(List<object?> array)
        {
            if (!_arrayContracts.TryGetValue(array, out var contract)) return [];
            lock (contract) return contract.ElementTypes;
        }

        private void StageArrayContract(List<object?> original, List<object?> normalized, string elementType)
        {
            var transaction = _validationTransaction!;
            if (!ReferenceEquals(original, normalized))
                foreach (var existing in GetArrayElementContracts(original))
                    transaction.Pending.Add(new(this, normalized, existing));
            transaction.Pending.Add(new(this, normalized, elementType));
        }

        internal object? NormalizePattern(object? value, string typeName)
        {
            if (typeName is "number" or "string" or "boolean" or "null" or "void" or "any" or "unknown" or "function"
                or "array" or "object" or "never")
                return Validate(value, typeName);
            var transaction = BeginValidation(out var ownsTransaction, out var checkpoint);
            try
            {
                return Validate(value, typeName);
            }
            finally
            {
                EndValidation(transaction, ownsTransaction, checkpoint, false);
            }
        }

        internal object? ValidateArrayWrite(List<object?> array, object? value)
        {
            var contracts = GetArrayElementContracts(array);
            if (contracts.Length == 0) return value;
            var transaction = BeginValidation(out var ownsTransaction, out var checkpoint);
            var success = false;
            try
            {
                foreach (var type in contracts) value = ValidateCore(value, type, 0);
                success = true;
                return value;
            }
            finally
            {
                EndValidation(transaction, ownsTransaction, checkpoint, success);
            }
        }

        internal IReadOnlyList<object?> ValidateArrayWrites(List<object?> array, IReadOnlyList<object?> values)
        {
            var contracts = GetArrayElementContracts(array);
            if (contracts.Length == 0 || values.Count == 0) return values;
            var transaction = BeginValidation(out var ownsTransaction, out var checkpoint);
            var success = false;
            try
            {
                List<object?>? normalized = null;
                for (var index = 0; index < values.Count; index++)
                {
                    var value = values[index];
                    foreach (var type in contracts) value = ValidateCore(value, type, 0);
                    if (Equals(value, values[index])) continue;
                    normalized ??= [.. values];
                    normalized[index] = value;
                }

                success = true;
                return normalized ?? values;
            }
            finally
            {
                EndValidation(transaction, ownsTransaction, checkpoint, success);
            }
        }

        private sealed class ArrayContract
        {
            public string[] ElementTypes = [];
        }

        private readonly record struct PendingArrayContract(
            ScriptBindingRegistry Registry,
            List<object?> Array,
            string ElementType);

        private sealed class ValidationTransaction
        {
            public readonly List<PendingArrayContract> Pending = [];
        }
    }
}
