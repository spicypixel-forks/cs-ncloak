using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Mono.Cecil;

namespace TiviT.NCloak.CloakTasks
{
    public class SupressIldasmTask : ICloakTask
    {
        /// <summary>
        /// Gets the task name.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get { return "Inserting anti-ILDASM code"; }
        }

        /// <summary>
        /// Runs the specified cloaking task.
        /// </summary>
        /// <param name="context">The running context of this cloak job.</param>
        public void RunTask(ICloakContext context)
        {
            Dictionary<string, AssemblyDefinition> assemblyCache = context.GetAssemblyDefinitions();
			var resolver = new DefaultAssemblyResolver();
            foreach (string assembly in assemblyCache.Keys)
            {
                AssemblyDefinition def = assemblyCache[assembly];
                Type si = typeof (SuppressIldasmAttribute);
                CustomAttribute found = null;
                foreach (CustomAttribute attr in def.CustomAttributes)
                {
                    if (attr.Constructor.DeclaringType.FullName == si.FullName)
                    {
                        found = attr;
                        break;
                    }
                }

                //Only add if it's not there already
                if (found == null)
                {
                    //Add one (using target module's mscorlib)
					var mscorlibRef = def.MainModule.AssemblyReferences.Where(a => a.Name.Contains("mscorlib")).Single();
					var mscorlib = resolver.Resolve(mscorlibRef);
					var supressAttrDef = mscorlib.MainModule.GetType("System.Runtime.CompilerServices.SuppressIldasmAttribute");
					MethodReference constructor = def.MainModule.Import(supressAttrDef.Methods.First(m => m.IsConstructor && !m.HasParameters));
                    CustomAttribute attr = new CustomAttribute(constructor);
                    def.CustomAttributes.Add(attr);
                }
            }

        }
    }
}
