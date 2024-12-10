using System.Collections.Generic;
using JetBrains.Annotations;
using Tools.Network;
using Tools.Security;
using Xunit;

namespace Tools.Tests.Security;

[TestSubject(typeof(PermissionTool))]
public class PermissionToolTest
{
    [Theory]
    [InlineData("p1", true, true)]
    [InlineData("p100", true, true)]
    [InlineData("p100", false, false)]
    [InlineData("p2", false, true)]
    [InlineData("p3", false, true)]
    public void VerifyPermissionTest(string permission, bool useAdminUser, bool expected)
    {
        var adminUser = new DAL.Entities.User
        {
            Admin = true,
            Permissions = new List<DAL.Entities.Permission>()
            {
                new DAL.Entities.Permission
                {
                    Key = "p1"
                },
                new DAL.Entities.Permission
                {
                    Key = "p2"
                }
            }
        };
        
        var user = new DAL.Entities.User
        {
            Admin = false,
            Permissions = new List<DAL.Entities.Permission>()
            {
                new DAL.Entities.Permission
                {
                    Key = "p1"
                },
                new DAL.Entities.Permission
                {
                    Key = "p2"
                },
                new DAL.Entities.Permission
                {
                    Key = "p3"
                }
            }
        };
        
        DAL.Entities.User testUser = useAdminUser ? adminUser : user;
        
        var result = PermissionTool.VerifyPermission(permission, testUser);
        Assert.Equal(expected, result);
    }
}
