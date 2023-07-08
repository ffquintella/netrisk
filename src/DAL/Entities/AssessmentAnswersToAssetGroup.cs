using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class AssessmentAnswersToAssetGroup
    {
        public int AssessmentAnswerId { get; set; }
        public int AssetGroupId { get; set; }
    }
}
