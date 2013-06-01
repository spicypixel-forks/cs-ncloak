using Mono.Cecil;
using Mono.Cecil.Rocks;
using TiviT.NCloak.Mapping;
using System;

namespace TiviT.NCloak.CloakTasks
{
    public class MappingTask : ICloakTask
    {
        /// <summary>
        /// Gets the task name.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get { return "Creating call map"; }
        }

        /// <summary>
        /// Runs the specified cloaking task.
        /// </summary>
        /// <param name="context">The running context of this cloak job.</param>
        public void RunTask(ICloakContext context)
        {
            //Get out if rename is turned off
            if (context.Settings.NoRename)
                return;
            //Go through the members and build up a mapping graph
            //If this is done then the members in the graph will be obfuscated, otherwise we'll 
            //just obfuscate private members

            //Loop through each assembly and process it
            foreach (AssemblyDefinition definition in context.GetAssemblyDefinitions().Values)
            {
                ProcessAssembly(context, definition);
            }
        }

		static bool ShouldObfuscate(ICloakContext context, TypeDefinition typeDefinition)
		{
			string assemblyName;
			if (typeDefinition.Scope is ModuleDefinition)
				assemblyName = ((ModuleDefinition)typeDefinition.Scope).Assembly.Name.FullName;
			else if (typeDefinition.Scope is AssemblyNameReference)
				assemblyName = ((AssemblyNameReference)typeDefinition.Scope).FullName;
			else {
				throw new InvalidOperationException("Unknown scope: " + typeDefinition.Scope);
			}

			//Check if this needs to be updated
			if (!context.MappingGraph.IsAssemblyMappingDefined(assemblyName))
				return false;

			// Non-nested types are either public or assembly
			if (!typeDefinition.IsNested) 
			{
				// Do not obfuscate system types like "<Module>"
				if (typeDefinition.Name.StartsWith("<"))
					return false;

				// Return whether to obfuscate all
				if (context.Settings.ObfuscateAllModifiers)
					return true;

				// Public types are not hidden
				if (typeDefinition.IsPublic)
					return false;

				// Non-public types are internal and honor this setting
				return context.Settings.ObfuscateInternalModifiers;
			}

			// Handle nested types

			// If the declaring type is hidden then so is the nested type
			if (ShouldObfuscate(context, typeDefinition.DeclaringType))
				return true;

			// The declaring type was not hidden so process each kind

			// Return whether to obfuscate all
			if (context.Settings.ObfuscateAllModifiers)
				return true;

			// Public or any kind of protected is not hidden
			if (typeDefinition.IsNestedPublic || typeDefinition.IsNestedFamily 
			    || typeDefinition.IsNestedFamilyAndAssembly || typeDefinition.IsNestedFamilyOrAssembly)
				return false;

			// Private is hidden
			if (typeDefinition.IsNestedPrivate)
				return true;

			// All that remains is assembly (e.g. internal) which honors this setting
			return context.Settings.ObfuscateInternalModifiers;
		}

		static bool ShouldObfuscate(ICloakContext context, IMemberDefinition memberDefinition, TypeDefinition declaringType = null)
		{
			if (memberDefinition == null)
				throw new ArgumentNullException("memberDefinition");

			if (declaringType == null)
				declaringType = memberDefinition.DeclaringType;

			string assemblyName;
			if (declaringType.Scope is ModuleDefinition)
				assemblyName = ((ModuleDefinition)declaringType.Scope).Assembly.Name.FullName;
			else if (declaringType.Scope is AssemblyNameReference)
				assemblyName = ((AssemblyNameReference)declaringType.Scope).FullName;
			else {
				throw new InvalidOperationException("Unknown scope: " + declaringType.Scope);
			}

			//Check if this needs to be updated
			if (!context.MappingGraph.IsAssemblyMappingDefined(assemblyName))
				return false;

			bool shouldObfuscateDeclaringType = ShouldObfuscate(context, declaringType);

			if (!shouldObfuscateDeclaringType && declaringType.IsInterface) // Don't obfuscate members if declaring type is not obfuscated and is an interface
				return false;

			var property = memberDefinition as PropertyDefinition;
			if (property != null) {
				if (property.GetMethod != null && property.SetMethod != null)
				{
					//Both parts need to be private
					return ShouldObfuscate(context, property.GetMethod) && ShouldObfuscate(context, property.SetMethod);
				}
				else if (property.GetMethod != null)
				{
					//Only the get is present - make sure it is private
					return ShouldObfuscate(context, property.GetMethod);
				}
				else if (property.SetMethod != null)
				{
					//Only the set is present - make sure it is private
					return ShouldObfuscate(context, property.SetMethod);
				}
			}

			var method = memberDefinition as MethodDefinition;
			if (method != null) {
				// Handle explicit interface methods
				if (IsExplicitlyImplemented(method)) {
					TypeReference iface;
					MethodReference ifaceMethod;
					GetInfoForExplicitlyImplementedMethod(method, out iface, out ifaceMethod);

					return ShouldObfuscate(context, ifaceMethod.Resolve());
				}

				// Handle implicit interface methods and overloads
				foreach (var iface in declaringType.Interfaces) {
					var ifaceMethod = iface.Resolve().Methods.FindMethod(method.Name, method.Parameters);
					if (ifaceMethod != null) {
						return ShouldObfuscate(context, ifaceMethod);
					}
				}

				// Handle virtual methods
				if(method.IsVirtual)
				{
					var baseMethod = FindBaseMethodDeclaration(declaringType, method);
					if (baseMethod != null) {
						return ShouldObfuscate(context, baseMethod);
					}
				}

				// Do not obfuscate delegate methods
				if (declaringType.BaseType != null && declaringType.BaseType.FullName == "System.MulticastDelegate")
					return false;

				if (shouldObfuscateDeclaringType)
					return true;

				if (context.Settings.ObfuscateAllModifiers)
					return true;

				if (method.IsPublic || method.IsFamily || method.IsFamilyAndAssembly || method.IsFamilyOrAssembly)
					return false;

				if (method.IsPrivate)
					return true;

				return context.Settings.ObfuscateInternalModifiers;
			}

			var field = memberDefinition as FieldDefinition;
			if (field != null) {
				if (shouldObfuscateDeclaringType)
					return true;

				if (context.Settings.ObfuscateAllModifiers)
					return true;

				if (field.IsPublic || field.IsFamily || field.IsFamilyAndAssembly || field.IsFamilyOrAssembly)
					return false;

				if (field.IsPrivate)
					return true;

				return context.Settings.ObfuscateInternalModifiers;
			}

			throw new InvalidOperationException("Unknown member definition: " + memberDefinition.GetType());
		}

		public static bool IsExplicitlyImplemented (MethodDefinition 
		                                            method) 
		{ 
			return method.IsPrivate && method.IsFinal && 
				method.IsVirtual; 
		} 

		public static void GetInfoForExplicitlyImplementedMethod ( 
		                                                          MethodDefinition method, out TypeReference iface, out MethodReference ifaceMethod) 
		{ 
			iface = null; 
			ifaceMethod = null; 
			if (method.Overrides.Count != 1) 
				throw new InvalidOperationException ("Could not determine interface type for explicitly-implemented interface member " + method.Name); 
			iface = method.Overrides [0].DeclaringType; 
			ifaceMethod = method.Overrides [0]; 
		} 

        /// <summary>
        /// Processes the assembly - goes through each member and applies a mapping.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="definition">The assembly definition.</param>
        private static void ProcessAssembly(ICloakContext context, AssemblyDefinition definition)
        {
            //Store whether to obfuscate all members
            bool obfuscateAll = context.Settings.ObfuscateAllModifiers;
			bool obfuscateInternal = context.Settings.ObfuscateInternalModifiers;

            //Set up the mapping graph
            AssemblyMapping assemblyMapping = context.MappingGraph.AddAssembly(definition);

            //Get a reference to the name manager
            NameManager nameManager = context.NameManager;

            //Go through each module
            foreach (ModuleDefinition moduleDefinition in definition.Modules)
            {
                //Go through each type
                foreach (TypeDefinition typeDefinition in moduleDefinition.GetAllTypes())
                {
					//Generic types are causing problems
					//Some members don't resolve
					if (typeDefinition.HasGenericParameters)
						continue;

                    //First of all - see if we've declared it already - if so get the existing reference
                    TypeMapping typeMapping = assemblyMapping.GetTypeMapping(typeDefinition);

                    if (typeMapping == null)
                    {
                        //We don't have it - get it
						if (ShouldObfuscate(context, typeDefinition))
                            typeMapping = assemblyMapping.AddType(typeDefinition,
                                                                  nameManager.GenerateName(NamingTable.Type));
                        else
                            typeMapping = assemblyMapping.AddType(typeDefinition, null);
                    }

                    //Go through each method
                    foreach (MethodDefinition methodDefinition in typeDefinition.Methods)
                    {
						// Handle props later
						if (methodDefinition.IsSpecialName 
						    && (methodDefinition.Name.Contains("get_") || methodDefinition.Name.Contains("set_")))
							continue;

                        //First of all - check if we've obfuscated it already - if we have then don't bother
                        if (typeMapping.HasMethodBeenObfuscated(methodDefinition.Name))
                            continue;

                        //We won't do constructors - causes issues
                        if (methodDefinition.IsConstructor)
                            continue;

						//Generic types are causing problems
						//Some members don't resolve
						if (methodDefinition.HasGenericParameters)
							continue;

						if (!ShouldObfuscate(context, methodDefinition))
							continue;

                        //We haven't - let's work out the obfuscated name
						if (obfuscateAll || obfuscateInternal) // all or internal must deal with inheritance
                        {
                            //Take into account whether this is overriden, or an interface implementation
                            if (methodDefinition.IsVirtual)
                            {
                                //We handle this differently - rather than creating a new name each time we need to reuse any already generated names
                                //We do this by firstly finding the root interface or object
                                TypeDefinition baseType = FindBaseTypeDeclaration(typeDefinition, methodDefinition);
                                if (baseType != null)
                                {
                                    //Find it in the mappings 
                                    TypeMapping baseTypeMapping = assemblyMapping.GetTypeMapping(baseType);
                                    if (baseTypeMapping != null)
                                    {
                                        //We found the type mapping - look up the name it uses for this method and use that
                                        if (baseTypeMapping.HasMethodMapping(methodDefinition))
                                            typeMapping.AddMethodMapping(methodDefinition, baseTypeMapping.GetObfuscatedMethodName(methodDefinition));
                                        else
                                        {
                                            //That's strange... we shouldn't get into here - but if we ever do then
                                            //we'll add the type mapping into both
											if (ShouldObfuscate(context, methodDefinition, baseType)) { // only if the base type member is obfuscated too do we do it
	                                            string obfuscatedName = nameManager.GenerateName(NamingTable.Method);
	                                            typeMapping.AddMethodMapping(methodDefinition, obfuscatedName);
	                                            baseTypeMapping.AddMethodMapping(methodDefinition, obfuscatedName);
											}
                                        }
                                    }
                                    else
                                    {
                                        //Otherwise add it into our list manually
                                        //at the base level first off
										if(ShouldObfuscate(context, baseType))
	                                        baseTypeMapping = assemblyMapping.AddType(baseType,
	                                                                  nameManager.GenerateName(NamingTable.Type));
										else
											baseTypeMapping = assemblyMapping.AddType(baseType, null);

										if (ShouldObfuscate(context, methodDefinition, baseType)) { // only if the base type member is obfuscated too do we do it
	                                        string obfuscatedName = nameManager.GenerateName(NamingTable.Method);
	                                        baseTypeMapping.AddMethodMapping(methodDefinition, obfuscatedName);
	                                        //Now at our implemented level
	                                        typeMapping.AddMethodMapping(methodDefinition, obfuscatedName);
										}
                                    }
                                }
                                else
                                {
                                    //We must be at the base already - add normally
                                    typeMapping.AddMethodMapping(methodDefinition,
                                                         nameManager.GenerateName(NamingTable.Method));
                                }
                            }
                            else { //Add normally
                                typeMapping.AddMethodMapping(methodDefinition,
                                                         nameManager.GenerateName(NamingTable.Method));
							}
                        }
						else
                            typeMapping.AddMethodMapping(methodDefinition, nameManager.GenerateName(NamingTable.Method));
                    }

                    //Properties
                    foreach (PropertyDefinition propertyDefinition in typeDefinition.Properties)
                    {
                        //First of all - check if we've obfuscated it already - if we have then don't bother
                        if (typeMapping.HasPropertyBeenObfuscated(propertyDefinition.Name))
                            continue;

						if (!ShouldObfuscate(context, propertyDefinition))
							continue;

                        //Go through the old fashioned way
                        if (obfuscateAll || obfuscateInternal)
                        {
                            if ((propertyDefinition.GetMethod != null && propertyDefinition.GetMethod.IsVirtual) || 
                                (propertyDefinition.SetMethod != null && propertyDefinition.SetMethod.IsVirtual))
                            {
                                //We handle this differently - rather than creating a new name each time we need to reuse any already generated names
                                //We do this by firstly finding the root interface or object
                                TypeDefinition baseType = FindBaseTypeDeclaration(typeDefinition, propertyDefinition);
                                if (baseType != null)
                                {
                                    //Find it in the mappings 
                                    TypeMapping baseTypeMapping = assemblyMapping.GetTypeMapping(baseType);
                                    if (baseTypeMapping != null)
                                    {
                                        //We found the type mapping - look up the name it uses for this property and use that
                                        if (baseTypeMapping.HasPropertyMapping(propertyDefinition))
                                            typeMapping.AddPropertyMapping(propertyDefinition, baseTypeMapping.GetObfuscatedPropertyName(propertyDefinition));
                                        else
                                        {
                                            //That's strange... we shouldn't get into here - but if we ever do then
                                            //we'll add the type mapping into both

											// Now that internal obfuscation is supported, this case is expected
											// because a mapping is only added if a method should be obfuscated.
											// The type may be mapped but the method is not.

											if (ShouldObfuscate(context, propertyDefinition, baseType)) { // only if the base type member is obfuscated too do we do it
	                                            string obfuscatedName = nameManager.GenerateName(NamingTable.Property);
	                                            typeMapping.AddPropertyMapping(propertyDefinition, obfuscatedName);
	                                            baseTypeMapping.AddPropertyMapping(propertyDefinition, obfuscatedName);
											}
                                        }
                                    }
                                    else
                                    {
                                        //Otherwise add it into our list manually
                                        //at the base level first off
										if (ShouldObfuscate(context, baseType))
	                                        baseTypeMapping = assemblyMapping.AddType(baseType,
	                                                                  nameManager.GenerateName(NamingTable.Type));
										else
											baseTypeMapping = assemblyMapping.AddType(baseType, null);

										if (ShouldObfuscate(context, propertyDefinition, baseType)) { // only if the base type member is obfuscated too do we do it
	                                        string obfuscatedName = nameManager.GenerateName(NamingTable.Property);
	                                        baseTypeMapping.AddPropertyMapping(propertyDefinition, obfuscatedName);
	                                        //Now at our implemented level
	                                        typeMapping.AddPropertyMapping(propertyDefinition, obfuscatedName);
										}
                                    }
                                }
                                else
                                {
                                    //We must be at the base already - add normally
                                    typeMapping.AddPropertyMapping(propertyDefinition,
                                                         nameManager.GenerateName(NamingTable.Property));
                                }
                            }
                            else
                                typeMapping.AddPropertyMapping(propertyDefinition,
                                                           nameManager.GenerateName(NamingTable.Property));
                        }
                        else 
                        {
                            //Both parts need to be private
                            typeMapping.AddPropertyMapping(propertyDefinition, nameManager.GenerateName(NamingTable.Property));
                        }
                    }

                    //Fields
                    foreach (FieldDefinition fieldDefinition in typeDefinition.Fields)
                    {
                        //First of all - check if we've obfuscated it already - if we have then don't bother
                        if (typeMapping.HasFieldBeenObfuscated(fieldDefinition.Name))
                            continue;

						if (ShouldObfuscate(context, fieldDefinition))
                        {
                            //Rename if private
                            typeMapping.AddFieldMapping(fieldDefinition, nameManager.GenerateName(NamingTable.Field));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recursively finds the base type declaration for the given method name.
        /// </summary>
        /// <param name="definition">The definition.</param>
        /// <param name="method">The method definition/reference.</param>
        /// <returns></returns>
        private static TypeDefinition FindBaseTypeDeclaration(TypeDefinition definition, MethodReference method)
        {
            //Search the interfaces first
            foreach (TypeReference tr in definition.Interfaces)
            {
                //Convert to a type definition
                TypeDefinition td = tr.GetTypeDefinition();
				if(td == null) {
					// Skip types not found
					continue;
				}

                MethodDefinition md = td.Methods.FindMethod(method.Name, method.Parameters);
                if (md != null)
                    return td;

                //Do a recursive search below
                TypeDefinition baseInterface = FindBaseTypeDeclaration(td, method);
                if (baseInterface != null)
                    return baseInterface;
            }

            //Search the base class
            TypeReference baseTr = definition.BaseType;
            if (baseTr != null)
            {
                TypeDefinition baseTd = baseTr.GetTypeDefinition();
                if (baseTd != null)
                {
                    MethodDefinition md = baseTd.Methods.FindMethod(method.Name, method.Parameters);
                    if (md != null)
                        return baseTd;

                    //Do a recursive search below
                    TypeDefinition baseClass = FindBaseTypeDeclaration(baseTd, method);
                    if (baseClass != null)
                        return baseClass;
                }
            }

            //We've exhausted all options
            return null;
        }

		/// <summary>
		/// Recursively finds the base type declaration for the given method name.
		/// </summary>
		/// <param name="definition">The definition.</param>
		/// <param name="method">The method definition/reference.</param>
		/// <returns></returns>
		private static MethodDefinition FindBaseMethodDeclaration(TypeDefinition definition, MethodReference method)
		{
			//Search the interfaces first
			foreach (TypeReference tr in definition.Interfaces)
			{
				// Convert to a type definition by actually resolving
				// the type. If we don't do a full resolve we cannot get module external
				// types like IDisposable to inspect their methods like Dispose.
				TypeDefinition td = tr.Resolve(); // tr.GetTypeDefinition();
				if(td == null) {
					// Skip types not found
					continue;
				}

				MethodDefinition md = td.Methods.FindMethod(method.Name, method.Parameters);
				if (md != null)
					return md;

				//Do a recursive search below
				var baseMethod = FindBaseMethodDeclaration(td, method);
				if (baseMethod != null)
					return baseMethod;
			}

			//Search the base class
			TypeReference baseTr = definition.BaseType;
			if (baseTr != null)
			{
				TypeDefinition baseTd = baseTr.GetTypeDefinition();
				if (baseTd != null)
				{
					MethodDefinition md = baseTd.Methods.FindMethod(method.Name, method.Parameters);
					if (md != null)
						return md;

					//Do a recursive search below
					var baseMethod = FindBaseMethodDeclaration(baseTd, method);
					if (baseMethod != null)
						return baseMethod;
				}
			}

			//We've exhausted all options
			return null;
		}

        /// <summary>
        /// Recursively finds the base type declaration for the given method name.
        /// </summary>
        /// <param name="definition">The definition.</param>
        /// <param name="property">The property definition/reference.</param>
        /// <returns></returns>
        private static TypeDefinition FindBaseTypeDeclaration(TypeDefinition definition, MemberReference property)
        {
            //Search the interfaces first
            foreach (TypeReference tr in definition.Interfaces)
            {
                //Convert to a type definition
                TypeDefinition td = tr.GetTypeDefinition();
				if(td == null) {
					// Skip types not found
					continue;
				}

                if (td.Properties.HasProperty(property.Name))
                    return td; 

                //Do a recursive search below
                TypeDefinition baseInterface = FindBaseTypeDeclaration(td, property);
                if (baseInterface != null)
                    return baseInterface;
            }

            //Search the base class
            TypeReference baseTr = definition.BaseType;
            if (baseTr != null)
            {
                TypeDefinition baseTd = baseTr.GetTypeDefinition();
                if (baseTd != null)
                {
                    if (baseTd.Properties.HasProperty(property.Name))
                        return baseTd;

                    //Do a recursive search below
                    TypeDefinition baseClass = FindBaseTypeDeclaration(baseTd, property);
                    if (baseClass != null)
                        return baseClass;
                }
            }

            //We've exhausted all options
            return null;
        }
    }
}
