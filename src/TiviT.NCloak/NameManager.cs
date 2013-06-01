using System;
using System.Collections.Generic;
namespace TiviT.NCloak
{
    public class NameManager
    {
        private readonly Dictionary<NamingTable, CharacterSet> namingTables;
		private readonly bool useAlphaCharacterSet = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="NameManager"/> class.
        /// </summary>
        public NameManager()
        {
            namingTables = new Dictionary<NamingTable, CharacterSet>();
        }

		public NameManager(bool useAlphaCharacterSet) : this()
		{
			this.useAlphaCharacterSet = useAlphaCharacterSet;
		}

        /// <summary>
        /// Sets the start character.
        /// </summary>
        /// <param name="table">The table.</param>
        /// <param name="characterSet">The new character set to use.</param>
        public void SetCharacterSet(NamingTable table, CharacterSet characterSet)
        {
            if (namingTables.ContainsKey(table))
                namingTables[table] = characterSet;
            else
                namingTables.Add(table, characterSet);
        }

        /// <summary>
        /// Generates a new unique name from the naming table.
        /// </summary>
        /// <param name="table">The table to generate a name from.</param>
        /// <returns>A unique name</returns>
        public string GenerateName(NamingTable table)
        {
            //Check the naming table exists
            if (!namingTables.ContainsKey(table))
				SetCharacterSet(table, useAlphaCharacterSet ? AlphaCharacterSet : DefaultCharacterSet);

            //Generate a new name
			string result = namingTables[table].Generate();
            if (table == NamingTable.Field) //For fields append an _ to make sure it differs from properties etc
                result = "_" + result;

			// Add more entropy to avoid collisions
			if (useAlphaCharacterSet)
				result = "_prv_" + result;

			return result;
        }

        /// <summary>
        /// Gets the default character set.
        /// </summary>
        /// <returns></returns>
        private static CharacterSet DefaultCharacterSet
        {
            get { return new CharacterSet('\u0800', '\u08ff'); }
        }

		private static CharacterSet AlphaCharacterSet
		{
			get { return new CharacterSet('a', 'z'); }
		}
    }
}
