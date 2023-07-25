using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class AssessmentAnswersToAsset
    {
        public int AssessmentAnswerId { get; set; }
        public int AssetId { get; set; }
    }
}
