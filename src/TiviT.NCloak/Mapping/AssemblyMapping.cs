using System.Collections.Generic;
using System;
using Mono.Cecil;
using System.Text;

namespace TiviT.NCloak.Mapping
{
    public class AssemblyMapping
    {
        private readonly string assemblyName;
        private readonly Dictionary<string, TypeMapping> typeMappingTable;
        private readonly Dictionary<string, string> obfuscatedToOriginalMapping;

        /// <summary>
        /// Initializes a new instance of the <see cref="AssemblyMapping"/> class.
        /// </summary>
        /// <param name="assemblyName">Name of the assembly.</param>
        public AssemblyMapping(string assemblyName)
        {
            this.assemblyName = assemblyName;
            typeMappingTable = new Dictionary<string, TypeMapping>();
            obfuscatedToOriginalMapping = new Dictionary<string, string>();
        }

        /// <summary>
        /// Gets the name of the assembly.
        /// </summary>
        /// <value>The name of the assembly.</value>
        public string AssemblyName
        {
            get { return assemblyName; }
        }

        /// <summary>
        /// Adds the type mapping to the assembly.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="obfuscatedTypeName">Name of the obfuscated type.</param>
        /// <returns></returns>
        public TypeMapping AddType(TypeReference type, string obfuscatedTypeName)
        {
			if (type == null)
				throw new ArgumentNullException("type");

			string typeName = GetTypeMappingName(type);

            TypeMapping typeMapping = new TypeMapping(typeName, obfuscatedTypeName);
            typeMappingTable.Add(typeName, typeMapping);
            
			// Add a reverse mapping
            if (!String.IsNullOrEmpty(obfuscatedTypeName))
				obfuscatedToOriginalMapping.Add(obfuscatedTypeName, typeName);
            
			return typeMapping;
        }

        /// <summary>
        /// Gets the type mapping.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns></returns>
        public TypeMapping GetTypeMapping(TypeReference type)
        {
			if (type == null)
				throw new ArgumentNullException("type");

			string typeName = GetTypeMappingName(type);

			TypeMapping mapping = null;
			typeMappingTable.TryGetValue(typeName, out mapping);

			return mapping;
        }

		string GetTypeMappingName(TypeReference type)
		{
			if (type == null)
				throw new ArgumentNullException("type");

			// Deobfuscate if mapped
			if (obfuscatedToOriginalMapping.ContainsKey(type.Name))
				return obfuscatedToOriginalMapping[type.Name];

			// Not nested and not obfuscated so return the easy case.
			// Note that only non-nested types have a populated namespace.
			if (!type.IsNested)
				return type.Namespace + "." + type.Name;

			// The type is nested
			return GetTypeMappingName(type.DeclaringType) + "/" + type.Name;
		}
    }
}
