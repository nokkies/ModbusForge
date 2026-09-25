using System.Collections.Generic;
using System.Linq;

namespace ModbusForge.Core.Plc.St
{
    internal abstract class StCall
    {
        /// <summary>Runs the call; a function returns its OUT (or first) output.</summary>
        public abstract PlcValue Invoke(PlcRuntime runtime);
    }

    /// <summary>A standard function (EF): inputs by name or position, outputs to the <c>=&gt;</c> targets.</summary>
    internal sealed class StFunctionCall : StCall
    {
        private readonly PlcBlockBehavior _behavior;
        private readonly PlcCall _call;
        private readonly PlcOperand[] _arguments;
        private readonly (int Output, PlcOperand Target)[] _targets;
        private readonly int _result;

        public StFunctionCall(PlcBlockBehavior behavior, string[] inputNames, PlcOperand[] arguments, string[] outputNames,
            IReadOnlyList<(int, PlcOperand)> targets, int result)
        {
            _behavior = behavior;
            _call = new PlcCall(inputNames, outputNames);
            _arguments = arguments;
            _targets = targets.ToArray();
            _result = result;
            foreach (var (output, target) in _targets) _call.SetOutputType(output, target.StaticType);
        }

        public override PlcValue Invoke(PlcRuntime runtime)
        {
            // Inputs go straight into the call object: an argument may call another
            // function, but every call in the code has its own call object, so reading
            // the arguments never re-enters this one.
            for (var i = 0; i < _arguments.Length; i++) _call.SetInput(i, _arguments[i].Read(runtime));

            _call.BeginCall();
            _call.Now = runtime.Now;
            _behavior.Execute(_call);
            if (_call.Failed)
            {
                runtime.CountStError();
                return default;
            }

            foreach (var (output, target) in _targets)
            {
                if (_call.WasWritten(output)) target.Write(runtime, _call.Output(output));
            }
            return _call.OutputCount > 0 && _call.WasWritten(_result) ? _call.Output(_result) : default;
        }
    }

    /// <summary>
    /// A function block instance call: the named inputs are written into the instance,
    /// the block runs (a standard EFB, or a DFB's own code), in/out parameters are copied
    /// back to their variables and the <c>=&gt;</c> outputs are read out of the instance.
    /// </summary>
    internal sealed class StInstanceCall : StCall
    {
        private readonly PlcReferenceOperand _instance;
        private readonly (PlcField Field, PlcOperand Value)[] _inputs;
        private readonly (PlcField Field, PlcOperand Target)[] _copyBack;
        private readonly (PlcField Field, PlcOperand Target)[] _outputs;

        // The DFB code last called, and per input and copy-back the in/out parameter it passes, if any.
        private PlcRuntime.DfbBody? _body;
        private PlcInOutRoot?[] _inputRoots = System.Array.Empty<PlcInOutRoot?>();
        private PlcInOutRoot?[] _copyBackRoots = System.Array.Empty<PlcInOutRoot?>();

        public StInstanceCall(PlcReferenceOperand instance, IEnumerable<(PlcField, PlcOperand)> inputs,
            IEnumerable<(PlcField, PlcOperand)> copyBack, IEnumerable<(PlcField, PlcOperand)> outputs)
        {
            _instance = instance;
            _inputs = inputs.ToArray();
            _copyBack = copyBack.ToArray();
            _outputs = outputs.ToArray();
        }

        public override PlcValue Invoke(PlcRuntime runtime)
        {
            var instance = _instance.Resolve(runtime);
            if (!instance.IsValid) return default;

            PlcRuntime.DfbBody? body = null;
            if (instance.Type.Dfb != null)
            {
                body = runtime.DfbBodyFor(instance);
                if (!ReferenceEquals(body, _body)) Prepare(body);
            }

            for (var i = 0; i < _inputs.Length; i++)
            {
                var (field, value) = _inputs[i];

                // A DFB's in/out parameter given a variable is bound to it for this call
                // (IEC 61131-3 passes VAR_IN_OUT by reference); anything else is copied.
                if (body != null && _inputRoots[i] is { } inOut)
                {
                    if (value is PlcReferenceOperand reference && reference.Resolve(runtime) is { IsValid: true } actual)
                    {
                        inOut.Bind(actual);
                        continue;
                    }
                    inOut.BindOwn();
                }
                runtime.Memory.Write(Field(instance, field), value.Read(runtime));
            }

            runtime.CallInstance(instance, body);

            for (var i = 0; i < _copyBack.Length; i++)
            {
                if (body != null && _copyBackRoots[i] is { ByReference: true }) continue;
                var (field, target) = _copyBack[i];
                target.Write(runtime, runtime.Memory.Read(Field(instance, field)));
            }
            foreach (var (field, target) in _outputs)
            {
                target.Write(runtime, runtime.Memory.Read(Field(instance, field)));
            }
            return default;
        }

        /// <summary>Which of this call's arguments pass the DFB's in/out parameters (worked out when the code first runs).</summary>
        private void Prepare(PlcRuntime.DfbBody body)
        {
            PlcInOutRoot? RootOf(PlcField field)
                => field.Direction == PlcParameterDirection.InOut && body.InOuts.TryGetValue(field.Name, out var root) ? root : null;
            _inputRoots = _inputs.Select(p => RootOf(p.Field)).ToArray();
            _copyBackRoots = _copyBack.Select(p => RootOf(p.Field)).ToArray();
            _body = body;
        }

        private static PlcLocation Field(PlcLocation instance, PlcField field)
            => new(instance.Space, instance.Offset + field.Offset, field.Type);
    }
}
