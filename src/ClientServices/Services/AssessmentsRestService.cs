using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using DAL.Entities;
using RestSharp;
using Serilog;
using System.Text.Json;
using ClientServices.Interfaces;
using Model.DTO;
using Model.Exceptions;
using ReliableRestClient.Exceptions;


namespace ClientServices.Services;

public class AssessmentsRestService: RestServiceBase, IAssessmentsService
{
    public  AssessmentsRestService(IRestService restService) : base(restService)
    {
        
    }
    
    public List<Assessment>? GetAssessments()
    {
        var client = RestService.GetClient();
        var request = new RestRequest("/Assessments");

        try
        {
            var response = client.Get<List<Assessment>>(request);
            return response;
        }
        catch (Exception ex)
        {
            Logger.Error("Error getting assessments: {0}", ex.Message);
            return null;
        }
        
    }

    public async Task<List<Assessment>?> GetAssessmentsAsync()
    {
        var client = RestService.GetClient();
        var request = new RestRequest("/Assessments");

        try
        {
            var response = await client.ExecuteGetAsync<List<Assessment>>(request);
            return response.Data;
        }
        catch (Exception ex)
        {
            Logger.Error("Error getting assessments: {0}", ex.Message);
            return null;
        }
    }

    public List<AssessmentRun>? GetAssessmentRuns(int assessmentId)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Runs");

        try
        {
            var response = client.Get<List<AssessmentRun>>(request);
            return response;
        }
        catch (Exception ex)
        {
            Logger.Error("Error getting assessments runs: {0}", ex.Message);
            return null;
        }
    }

    // TODO: Implement this
    public void Update(Assessment assessment)
    {
        throw new NotImplementedException();
    }

    public void UpdateAssessmentRun(AssessmentRunDto assessmentRun)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentRun.AssessmentId}/Runs/{assessmentRun.Id}");
        
        request.AddJsonBody(assessmentRun);
        
        try
        {
            var response = client.Put(request);
            
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error updating assessment run: {0}", response.ErrorMessage);
            }
            
        }
        catch (Exception ex)
        {
            Logger.Error("Error updating assessment run: {0}", ex.Message);
        }
    }

    public void DeleteAllAnswers(int assessmentId, int runId)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Runs/{runId}/answers");
        
        try
        {
            var response = client.Delete(request);
            
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting assessment run answers: {0}", response.ErrorMessage);
            }
            
        }
        catch (Exception ex)
        {
            Logger.Error("Error deleting assessment run answers: {0}", ex.Message);
        }
    }

    public void DeleteRun(int assessmentId, int runId)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Runs/{runId}");
        
        try
        {
            var response = client.Delete(request);
            
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Logger.Error("Error deleting assessment run: {0}", response.ErrorMessage);
                throw new RestException((int) response.StatusCode, "Error deleting assessment run");
            }
            
        }
        catch (Exception ex)
        {
            Logger.Error("Error deleting assessment run: {0}", ex.Message);
            throw new Exception("unknown error deleting assessment run");
        }
    }

    public List<AssessmentRunsAnswer>? GetAssessmentRunAnsers(int assessmentId, int runId)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Runs/{runId}/Answers");

        try
        {
            var response = client.Get<List<AssessmentRunsAnswer>>(request);
            return response;
        }
        catch (Exception ex)
        {
            Logger.Error("Error getting assessments runs: {0}", ex.Message);
            return null;
        } 
    }

    public Tuple<int, Assessment?> Create(Assessment assessment)
    {
        using var client = RestService.GetClient();
        var request = new RestRequest("/Assessments");
        request.AddJsonBody(assessment);
        
        try
        {
            var response = client.Post<Assessment>(request);
            
            if (response!= null)
            {
                
                return new Tuple<int, Assessment?>(0, response);
            }
            
            return new Tuple<int, Assessment?>(-1, null);
            
        }
        catch (Exception ex)
        {
            Logger.Error("Error creating assessment: {0}", ex.Message);
            return new Tuple<int, Assessment?>(-1, null);
        }
        
    }

    public AssessmentRun? CreateAssessmentRun(AssessmentRunDto assessmentRun)
    {
        using var client = RestService.GetClient();
        
        if(assessmentRun.AssessmentId <= 0 || assessmentRun.AnalystId <= 0 || assessmentRun.EntityId <= 0)
            throw new InvalidParameterException("assessmentRun","Ids must be set");

        
        var request = new RestRequest($"/Assessments/{assessmentRun.AssessmentId}/Runs");
        
        request.AddJsonBody(assessmentRun);
        
        try
        {
            var response = client.Post<AssessmentRunDto>(request);
            
            if (response!= null)
            {

                return response;
            }

            throw new HttpRequestException("Error creating assessment run");

        }
        catch (Exception ex)
        {
            Logger.Error("Error creating assessment run : {0}", ex.Message);
            throw;
        }
        
    }

    public AssessmentRunsAnswer CreateRunAnswer(int assessmentId, AssessmentRunsAnswerDto answer)
    {
        using var client = RestService.GetClient();
        
        if(answer.AnswerId <= 0 || answer.QuestionId <= 0 || answer.RunId <= 0)
            throw new InvalidParameterException("AssessmentRunsAnswerDto","Ids must be set");

        
        var request = new RestRequest($"/Assessments/{assessmentId}/Runs/{answer.RunId}/Answers");
        
        request.AddJsonBody(answer);
        
        try
        {
            var response = client.Post<AssessmentRunsAnswer>(request);
            
            if (response!= null)
            {

                return response;
            }

            throw new HttpRequestException("Error creating assessment run answer");

        }
        catch (Exception ex)
        {
            Logger.Error("Error creating assessment run answer : {0}", ex.Message);
            throw;
        }
    }

    public Tuple<int, List<AssessmentAnswer>?> CreateAnswers(int assessmentId,
        int questionId,
        List<AssessmentAnswer> answers)
    {
        if (answers.Count == 0) return new Tuple<int, List<AssessmentAnswer>?>(0, answers);
        
        var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Questions/{questionId}/Answers");

        foreach (var answer in answers)
        {
            if (answer.Id != 0) 
                return new Tuple<int, List<AssessmentAnswer>?>(-1, null);
            answer.AssessmentId = assessmentId;
            answer.QuestionId = questionId;

        }
        
        
        
        request.AddJsonBody(answers);

        try
        {
            var response = client.Post(request);
            
            if (response.IsSuccessful && response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var questionResponse = JsonSerializer.Deserialize<List<AssessmentAnswer>?>(response.Content!, options);
                return new Tuple<int, List<AssessmentAnswer>?>(0, questionResponse);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return new Tuple<int, List<AssessmentAnswer>?>(1, null);    
            }
            
            return new Tuple<int, List<AssessmentAnswer>?>(-1, null);
        }
        catch (Exception ex)
        {
            Logger.Error("Error creating answers: {0}", ex.Message);
        }
        
        return new Tuple<int, List<AssessmentAnswer>?>(-1, null);
    }

    public Tuple<int, List<AssessmentAnswer>?> UpdateAnswers(int assessmentId,
        int questionId,
        List<AssessmentAnswer> answers)
    {

        if (answers.Count == 0) return new Tuple<int, List<AssessmentAnswer>?>(0, answers);
        
        var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Questions/{questionId}/Answers");

        foreach (var answer in answers)
        {
            if (answer.Id == 0) 
                return new Tuple<int, List<AssessmentAnswer>?>(-1, null);
            answer.AssessmentId = assessmentId;
            answer.QuestionId = questionId;
        }
        
        request.AddJsonBody(answers);

        try
        {
            var response = client.Put(request);
            
            if (response.IsSuccessful && response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var questionResponse = JsonSerializer.Deserialize<List<AssessmentAnswer>?>(response.Content!, options);
                return new Tuple<int, List<AssessmentAnswer>?>(0, questionResponse);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return new Tuple<int, List<AssessmentAnswer>?>(1, null);    
            }
            
            return new Tuple<int, List<AssessmentAnswer>?>(-1, null);
        }
        catch (Exception ex)
        {
            Logger.Error("Error updating answers: {0}", ex.Message);
        }
        return new Tuple<int, List<AssessmentAnswer>?>(-1, null);
    }
    
    public int DeleteAnswers(int assessmentId,
        int questionId,
        List<AssessmentAnswer> answers)
    {

        if (answers.Count == 0) return 0;
        
        var client = RestService.GetClient();

        foreach (var answer in answers)
        {
            var request = new RestRequest($"/Assessments/{assessmentId}/Questions/{questionId}/Answers/{answer.Id}");
            var result = client.Delete<string>(request);
            if (result != "Ok")
            {
                Logger.Error("Error deleting answer {0}" , answer.Id);
                return -1;
            }
        }

        return 0;
    }
    
    public Tuple<int, AssessmentQuestion?> CreateQuestion(int assessmentId, AssessmentQuestion question)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Questions");
        request.AddJsonBody(question);
        
        try
        {
            var response = client.Post(request);
            
            if (response.IsSuccessful && response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var questionResponse = JsonSerializer.Deserialize<AssessmentQuestion>(response.Content!, options);
                return new Tuple<int, AssessmentQuestion?>(0, questionResponse);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return new Tuple<int, AssessmentQuestion?>(1, null);    
            }
            
            return new Tuple<int, AssessmentQuestion?>(-1, null);
            
        }
        catch (Exception ex)
        {
            Logger.Error("Error creating assessment question: {0}", ex.Message);
            return new Tuple<int, AssessmentQuestion?>(-1, null);
        }
    }
    
    public Tuple<int, AssessmentQuestion?> UpdateQuestion(int assessmentId, AssessmentQuestion question)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Questions");
        request.AddJsonBody(question);
        
        try
        {
            var response = client.Put(request);
            
            if (response.IsSuccessful && response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var questionResponse = JsonSerializer.Deserialize<AssessmentQuestion>(response.Content!, options);
                return new Tuple<int, AssessmentQuestion?>(0, questionResponse);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return new Tuple<int, AssessmentQuestion?>(1, null);    
            }
            
            return new Tuple<int, AssessmentQuestion?>(-1, null);
            
        }
        catch (Exception ex)
        {
            Logger.Error("Error updating assessment question: {0}", ex.Message);
            return new Tuple<int, AssessmentQuestion?>(-1, null);
        }
    }
    
    public int DeleteQuestion(int assessmentId, int assessmentQuestionId)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Questions/{assessmentQuestionId}");

        try
        {
            var response = client.Delete(request);
            if(response.IsSuccessful && response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return 0;
            }

            return -1;
        }
        catch (Exception ex)
        {
            Logger.Error("Error deleting assessment question: {0} message: {1}", assessmentId, ex.Message);
            return -1;
        }
    }
    
    public int Delete(int assessmentId)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}");

        try
        {
            var response = client.Delete(request);
            if(response.IsSuccessful && response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                return 0;
            }

            return -1;
        }
        catch (Exception ex)
        {
            Logger.Error("Error deleting assessment: {0} message: {1}", assessmentId, ex.Message);
            return -1;
        }
    }
    
    public List<AssessmentQuestion>? GetAssessmentQuestions(int assessmentId)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Questions");

        try
        {
            var response = client.Get<List<AssessmentQuestion>>(request);
            return response;
        }
        catch (Exception ex)
        {
            Logger.Error("Error getting assessments questions: {0}", ex.Message);
            return null;
        }
    }
    
    public List<AssessmentAnswer>? GetAssessmentAnswers(int assessmentId)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Answers");

        try
        {
            var response = client.Get<List<AssessmentAnswer>>(request);
            return response;
        }
        catch (Exception ex)
        {
            Logger.Error("Error getting assessments answers: {0}", ex.Message);
            return null;
        }
    }

}