using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class Track6Phase2NamingUniformization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FaceIDUsers_user_UserId",
                table: "FaceIDUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_IncidentToIncidentResponsePlan_IncidentResponsePlans_Inciden~",
                table: "IncidentToIncidentResponsePlan");

            migrationBuilder.DropForeignKey(
                name: "FK_IncidentToIncidentResponsePlan_Incidents_IncidentId",
                table: "IncidentToIncidentResponsePlan");

            migrationBuilder.DropPrimaryKey(
                name: "PK_IncidentToIncidentResponsePlan",
                table: "IncidentToIncidentResponsePlan");

            migrationBuilder.RenameTable(
                name: "Incidents",
                newName: "incidents");

            migrationBuilder.RenameTable(
                name: "IncidentToIncidentResponsePlan",
                newName: "incident_to_incident_response_plan");

            migrationBuilder.RenameTable(
                name: "IncidentResponsePlanTasks",
                newName: "incident_response_plan_tasks");

            migrationBuilder.RenameTable(
                name: "IncidentResponsePlanTaskExecutions",
                newName: "incident_response_plan_task_executions");

            migrationBuilder.RenameTable(
                name: "IncidentResponsePlans",
                newName: "incident_response_plans");

            migrationBuilder.RenameTable(
                name: "IncidentResponsePlanExecutions",
                newName: "incident_response_plan_executions");

            migrationBuilder.RenameTable(
                name: "FixRequest",
                newName: "fix_requests");

            migrationBuilder.RenameTable(
                name: "FaceIDUsers",
                newName: "face_id_users");

            migrationBuilder.RenameTable(
                name: "BiometricTransaction",
                newName: "biometric_transactions");

            migrationBuilder.RenameColumn(
                name: "actionId",
                table: "vulnerabilities_to_actions",
                newName: "action_id");

            migrationBuilder.RenameColumn(
                name: "vulnerabilityId",
                table: "vulnerabilities_to_actions",
                newName: "vulnerability_id");

            migrationBuilder.RenameColumn(
                name: "fileId",
                table: "reports",
                newName: "file_id");

            migrationBuilder.RenameColumn(
                name: "creatorId",
                table: "reports",
                newName: "creator_id");

            migrationBuilder.RenameColumn(
                name: "creationDate",
                table: "reports",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "Message",
                table: "messages",
                newName: "message");

            migrationBuilder.RenameIndex(
                name: "IX_Incidents_UpdatedById",
                table: "incidents",
                newName: "IX_incidents_UpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_Incidents_ReportEntityId",
                table: "incidents",
                newName: "IX_incidents_ReportEntityId");

            migrationBuilder.RenameIndex(
                name: "IX_Incidents_ImpactedEntityId",
                table: "incidents",
                newName: "IX_incidents_ImpactedEntityId");

            migrationBuilder.RenameIndex(
                name: "IX_Incidents_CreatedById",
                table: "incidents",
                newName: "IX_incidents_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_Incidents_AssignedToId",
                table: "incidents",
                newName: "IX_incidents_AssignedToId");

            migrationBuilder.RenameColumn(
                name: "OS",
                table: "hosts",
                newName: "os");

            migrationBuilder.RenameColumn(
                name: "FQDN",
                table: "hosts",
                newName: "fqdn");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentToIncidentResponsePlan_IncidentResponsePlanId",
                table: "incident_to_incident_response_plan",
                newName: "IX_incident_to_incident_response_plan_IncidentResponsePlanId");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTasks_UpdatedById",
                table: "incident_response_plan_tasks",
                newName: "IX_incident_response_plan_tasks_UpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTasks_PlanId",
                table: "incident_response_plan_tasks",
                newName: "IX_incident_response_plan_tasks_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTasks_LastTestedById",
                table: "incident_response_plan_tasks",
                newName: "IX_incident_response_plan_tasks_LastTestedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTasks_CreatedById",
                table: "incident_response_plan_tasks",
                newName: "IX_incident_response_plan_tasks_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTasks_AssignedToId",
                table: "incident_response_plan_tasks",
                newName: "IX_incident_response_plan_tasks_AssignedToId");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecutions_TaskId",
                table: "incident_response_plan_task_executions",
                newName: "IX_incident_response_plan_task_executions_TaskId");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecutions_PlanExecutionId",
                table: "incident_response_plan_task_executions",
                newName: "IX_incident_response_plan_task_executions_PlanExecutionId");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecutions_LastUpdatedById",
                table: "incident_response_plan_task_executions",
                newName: "IX_incident_response_plan_task_executions_LastUpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecutions_ExecutedById",
                table: "incident_response_plan_task_executions",
                newName: "IX_incident_response_plan_task_executions_ExecutedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanTaskExecutions_CreatedById",
                table: "incident_response_plan_task_executions",
                newName: "IX_incident_response_plan_task_executions_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlans_UpdatedById",
                table: "incident_response_plans",
                newName: "IX_incident_response_plans_UpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlans_LastTestedById",
                table: "incident_response_plans",
                newName: "IX_incident_response_plans_LastTestedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlans_LastReviewedById",
                table: "incident_response_plans",
                newName: "IX_incident_response_plans_LastReviewedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlans_LastExercisedById",
                table: "incident_response_plans",
                newName: "IX_incident_response_plans_LastExercisedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlans_CreatedById",
                table: "incident_response_plans",
                newName: "IX_incident_response_plans_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlans_ApprovedById",
                table: "incident_response_plans",
                newName: "IX_incident_response_plans_ApprovedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanExecutions_PlanId",
                table: "incident_response_plan_executions",
                newName: "IX_incident_response_plan_executions_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanExecutions_LastUpdatedById",
                table: "incident_response_plan_executions",
                newName: "IX_incident_response_plan_executions_LastUpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanExecutions_ExecutedById",
                table: "incident_response_plan_executions",
                newName: "IX_incident_response_plan_executions_ExecutedById");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentResponsePlanExecutions_CreatedById",
                table: "incident_response_plan_executions",
                newName: "IX_incident_response_plan_executions_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_FaceIDUsers_UserId",
                table: "face_id_users",
                newName: "IX_face_id_users_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_FaceIDUsers_LastUpdateUserId",
                table: "face_id_users",
                newName: "IX_face_id_users_LastUpdateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_BiometricTransaction_UserId",
                table: "biometric_transactions",
                newName: "IX_biometric_transactions_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_BiometricTransaction_FaceIdUserId",
                table: "biometric_transactions",
                newName: "IX_biometric_transactions_FaceIdUserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_incident_to_incident_response_plan",
                table: "incident_to_incident_response_plan",
                columns: new[] { "IncidentId", "IncidentResponsePlanId" });

            migrationBuilder.AddForeignKey(
                name: "FK_face_id_users_user_UserId",
                table: "face_id_users",
                column: "UserId",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_incident_to_incident_response_plan_incident_response_plans_I~",
                table: "incident_to_incident_response_plan",
                column: "IncidentResponsePlanId",
                principalTable: "incident_response_plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_incident_to_incident_response_plan_incidents_IncidentId",
                table: "incident_to_incident_response_plan",
                column: "IncidentId",
                principalTable: "incidents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_face_id_users_user_UserId",
                table: "face_id_users");

            migrationBuilder.DropForeignKey(
                name: "FK_incident_to_incident_response_plan_incident_response_plans_I~",
                table: "incident_to_incident_response_plan");

            migrationBuilder.DropForeignKey(
                name: "FK_incident_to_incident_response_plan_incidents_IncidentId",
                table: "incident_to_incident_response_plan");

            migrationBuilder.DropPrimaryKey(
                name: "PK_incident_to_incident_response_plan",
                table: "incident_to_incident_response_plan");

            migrationBuilder.RenameTable(
                name: "incidents",
                newName: "Incidents");

            migrationBuilder.RenameTable(
                name: "incident_to_incident_response_plan",
                newName: "IncidentToIncidentResponsePlan");

            migrationBuilder.RenameTable(
                name: "incident_response_plans",
                newName: "IncidentResponsePlans");

            migrationBuilder.RenameTable(
                name: "incident_response_plan_tasks",
                newName: "IncidentResponsePlanTasks");

            migrationBuilder.RenameTable(
                name: "incident_response_plan_task_executions",
                newName: "IncidentResponsePlanTaskExecutions");

            migrationBuilder.RenameTable(
                name: "incident_response_plan_executions",
                newName: "IncidentResponsePlanExecutions");

            migrationBuilder.RenameTable(
                name: "fix_requests",
                newName: "FixRequest");

            migrationBuilder.RenameTable(
                name: "face_id_users",
                newName: "FaceIDUsers");

            migrationBuilder.RenameTable(
                name: "biometric_transactions",
                newName: "BiometricTransaction");

            migrationBuilder.RenameColumn(
                name: "action_id",
                table: "vulnerabilities_to_actions",
                newName: "actionId");

            migrationBuilder.RenameColumn(
                name: "vulnerability_id",
                table: "vulnerabilities_to_actions",
                newName: "vulnerabilityId");

            migrationBuilder.RenameColumn(
                name: "file_id",
                table: "reports",
                newName: "fileId");

            migrationBuilder.RenameColumn(
                name: "creator_id",
                table: "reports",
                newName: "creatorId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "reports",
                newName: "creationDate");

            migrationBuilder.RenameColumn(
                name: "message",
                table: "messages",
                newName: "Message");

            migrationBuilder.RenameIndex(
                name: "IX_incidents_UpdatedById",
                table: "Incidents",
                newName: "IX_Incidents_UpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_incidents_ReportEntityId",
                table: "Incidents",
                newName: "IX_Incidents_ReportEntityId");

            migrationBuilder.RenameIndex(
                name: "IX_incidents_ImpactedEntityId",
                table: "Incidents",
                newName: "IX_Incidents_ImpactedEntityId");

            migrationBuilder.RenameIndex(
                name: "IX_incidents_CreatedById",
                table: "Incidents",
                newName: "IX_Incidents_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_incidents_AssignedToId",
                table: "Incidents",
                newName: "IX_Incidents_AssignedToId");

            migrationBuilder.RenameColumn(
                name: "os",
                table: "hosts",
                newName: "OS");

            migrationBuilder.RenameColumn(
                name: "fqdn",
                table: "hosts",
                newName: "FQDN");

            migrationBuilder.RenameIndex(
                name: "IX_incident_to_incident_response_plan_IncidentResponsePlanId",
                table: "IncidentToIncidentResponsePlan",
                newName: "IX_IncidentToIncidentResponsePlan_IncidentResponsePlanId");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plans_UpdatedById",
                table: "IncidentResponsePlans",
                newName: "IX_IncidentResponsePlans_UpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plans_LastTestedById",
                table: "IncidentResponsePlans",
                newName: "IX_IncidentResponsePlans_LastTestedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plans_LastReviewedById",
                table: "IncidentResponsePlans",
                newName: "IX_IncidentResponsePlans_LastReviewedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plans_LastExercisedById",
                table: "IncidentResponsePlans",
                newName: "IX_IncidentResponsePlans_LastExercisedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plans_CreatedById",
                table: "IncidentResponsePlans",
                newName: "IX_IncidentResponsePlans_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plans_ApprovedById",
                table: "IncidentResponsePlans",
                newName: "IX_IncidentResponsePlans_ApprovedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_tasks_UpdatedById",
                table: "IncidentResponsePlanTasks",
                newName: "IX_IncidentResponsePlanTasks_UpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_tasks_PlanId",
                table: "IncidentResponsePlanTasks",
                newName: "IX_IncidentResponsePlanTasks_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_tasks_LastTestedById",
                table: "IncidentResponsePlanTasks",
                newName: "IX_IncidentResponsePlanTasks_LastTestedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_tasks_CreatedById",
                table: "IncidentResponsePlanTasks",
                newName: "IX_IncidentResponsePlanTasks_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_tasks_AssignedToId",
                table: "IncidentResponsePlanTasks",
                newName: "IX_IncidentResponsePlanTasks_AssignedToId");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_task_executions_TaskId",
                table: "IncidentResponsePlanTaskExecutions",
                newName: "IX_IncidentResponsePlanTaskExecutions_TaskId");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_task_executions_PlanExecutionId",
                table: "IncidentResponsePlanTaskExecutions",
                newName: "IX_IncidentResponsePlanTaskExecutions_PlanExecutionId");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_task_executions_LastUpdatedById",
                table: "IncidentResponsePlanTaskExecutions",
                newName: "IX_IncidentResponsePlanTaskExecutions_LastUpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_task_executions_ExecutedById",
                table: "IncidentResponsePlanTaskExecutions",
                newName: "IX_IncidentResponsePlanTaskExecutions_ExecutedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_task_executions_CreatedById",
                table: "IncidentResponsePlanTaskExecutions",
                newName: "IX_IncidentResponsePlanTaskExecutions_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_executions_PlanId",
                table: "IncidentResponsePlanExecutions",
                newName: "IX_IncidentResponsePlanExecutions_PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_executions_LastUpdatedById",
                table: "IncidentResponsePlanExecutions",
                newName: "IX_IncidentResponsePlanExecutions_LastUpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_executions_ExecutedById",
                table: "IncidentResponsePlanExecutions",
                newName: "IX_IncidentResponsePlanExecutions_ExecutedById");

            migrationBuilder.RenameIndex(
                name: "IX_incident_response_plan_executions_CreatedById",
                table: "IncidentResponsePlanExecutions",
                newName: "IX_IncidentResponsePlanExecutions_CreatedById");

            migrationBuilder.RenameIndex(
                name: "IX_face_id_users_UserId",
                table: "FaceIDUsers",
                newName: "IX_FaceIDUsers_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_face_id_users_LastUpdateUserId",
                table: "FaceIDUsers",
                newName: "IX_FaceIDUsers_LastUpdateUserId");

            migrationBuilder.RenameIndex(
                name: "IX_biometric_transactions_UserId",
                table: "BiometricTransaction",
                newName: "IX_BiometricTransaction_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_biometric_transactions_FaceIdUserId",
                table: "BiometricTransaction",
                newName: "IX_BiometricTransaction_FaceIdUserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_IncidentToIncidentResponsePlan",
                table: "IncidentToIncidentResponsePlan",
                columns: new[] { "IncidentId", "IncidentResponsePlanId" });

            migrationBuilder.AddForeignKey(
                name: "FK_FaceIDUsers_user_UserId",
                table: "FaceIDUsers",
                column: "UserId",
                principalTable: "user",
                principalColumn: "value",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentToIncidentResponsePlan_IncidentResponsePlans_Inciden~",
                table: "IncidentToIncidentResponsePlan",
                column: "IncidentResponsePlanId",
                principalTable: "IncidentResponsePlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentToIncidentResponsePlan_Incidents_IncidentId",
                table: "IncidentToIncidentResponsePlan",
                column: "IncidentId",
                principalTable: "Incidents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
