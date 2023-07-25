using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class SavedTableDisplaySetting
    {
        public int Id { get; set; }
        /// <summary>
        /// ID of the user who created the save
        /// </summary>
        public int UserId { get; set; }
        /// <summary>
        /// Name of the view like plan_mitigation or asset_edit to be able to get it for the table where it is used
        /// </summary>
        public string View { get; set; } = null!;
        /// <summary>
        /// Visibility of the save. Only used if there are multiple saves for the same view.
        /// </summary>
        public string? Visibility { get; set; }
        /// <summary>
        /// Name of the save. Only used if there are multiple saves for the same view.
        /// </summary>
        public string? Name { get; set; }
        public string DisplaySettings { get; set; } = null!;
    }
}
