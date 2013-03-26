using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Emit.InterfaceProxy
{
    public abstract class FactoryProxy<T> : Proxy<T> where T : class
    {
        private readonly MethodInfo _getInstance = typeof(FactoryProxy<T>).GetMethod("GetInstance", BindingFlags.Instance | BindingFlags.NonPublic);

        protected abstract T GetInstance(string name, Type[] types);

        protected override void BuildMethodBody(MethodInfo info, MethodBuilder method, LocalBuilder types, LocalBuilder returnValue, Label endOfMethod)
        {
            ILGenerator il = method.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);

            // pass method name to "GetInstance" method
            il.Emit(OpCodes.Ldstr, info.Name);

            // pass types to "GetInstance" method
            types.Load(il);

            il.Emit(OpCodes.Callvirt, _getInstance);

            LocalBuilder instance = il.DeclareLocal(_interfaceType);

            instance.Save(il);

            instance.Load(il);

            // pass arguments ( starting with 1 )
            for (int i = 1; i <= info.GetParameters().Length; i++)
            {
                il.Emit(OpCodes.Ldarg_S, i);
            }

            il.Emit(OpCodes.Callvirt, info);

            returnValue.Save(il);
        }
    }
}
