using System.Reflection;
using System.Reflection.Emit;

namespace Emit.InterfaceProxy
{
    public abstract class FacadeProxy<T> : Proxy<T> where T : class
    {
        private readonly MethodInfo _internalExecute = typeof(FacadeProxy<T>).GetMethod("InternalExecute", BindingFlags.Instance | BindingFlags.NonPublic);

        protected abstract object InternalExecute(string name, object[] args);

        protected override void BuildMethodBody(MethodInfo info, MethodBuilder method, LocalBuilder types, LocalBuilder returnValue, Label endOfMethod)
        {
            ILGenerator il = method.GetILGenerator();
            ParameterInfo[] parameters = info.GetParameters();
            int argsLength = parameters.Length;

            LocalBuilder values = il.DeclareLocal(typeof(object[]));

            values.DeclareArray<object>(il, argsLength);
            int i = 0;
            foreach (ParameterInfo parameter in parameters)
            {
                values.Load(il);
                il.Emit(OpCodes.Ldc_I4_S, i++); // set the index to array

                il.Emit(OpCodes.Ldarg_S, i);// need the +1 value to load from method args

                if (!parameter.ParameterType.IsClass)
                {
                    il.Emit(OpCodes.Box, parameter.ParameterType);
                }
                il.Emit(OpCodes.Stelem_Ref);
            }

            il.Emit(OpCodes.Ldarg_0);

            // pass method name to "InternalExecute" method
            il.Emit(OpCodes.Ldstr, info.Name);

            values.Load(il);

            il.Emit(OpCodes.Callvirt, _internalExecute);

            if (returnValue != null)
            {
                if (!info.ReturnType.IsClass)
                {
                    il.Emit(OpCodes.Unbox_Any, info.ReturnType);
                }
            }
            else
            {
                il.Emit(OpCodes.Pop);
            }

            returnValue.Save(il);
        }

    }
}
