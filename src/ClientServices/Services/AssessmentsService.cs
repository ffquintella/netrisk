using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using DAL.Entities;
using RestSharp;
using Serilog;
using System.Text.Json;
using ClientServices.Interfaces;


namespace ClientServices.Services;

public class AssessmentsService: ServiceBase, IAssessmentsService
{
    public AssessmentsService(IRestService restService) : base(restService)
    {
        
    }
    
    public List<Assessment>? GetAssessments()
    {
        var client = _restService.GetClient();
        var request = new RestRequest("/Assessments");

        try
        {
            var response = client.Get<List<Assessment>>(request);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error("Error getting assessments: {0}", ex.Message);
            return null;
        }
        
    }

    public Tuple<int, Assessment?> Create(Assessment assessment)
    {
        var client = _restService.GetClient();
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
            _logger.Error("Error creating assessment: {0}", ex.Message);
            return new Tuple<int, Assessment?>(-1, null);
        }
        
    }

    public Tuple<int, List<AssessmentAnswer>?> CreateAnswers(int assessmentId,
        int questionId,
        List<AssessmentAnswer> answers)
    {
        if (answers.Count == 0) return new Tuple<int, List<AssessmentAnswer>?>(0, answers);
        
        var client = _restService.GetClient();
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
            _logger.Error("Error creating answers: {0}", ex.Message);
        }
        
        return new Tuple<int, List<AssessmentAnswer>?>(-1, null);
    }

    public Tuple<int, List<AssessmentAnswer>?> UpdateAnswers(int assessmentId,
        int questionId,
        List<AssessmentAnswer> answers)
    {

        if (answers.Count == 0) return new Tuple<int, List<AssessmentAnswer>?>(0, answers);
        
        var client = _restService.GetClient();
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
            _logger.Error("Error updating answers: {0}", ex.Message);
        }
        return new Tuple<int, List<AssessmentAnswer>?>(-1, null);
    }
    
    public int DeleteAnswers(int assessmentId,
        int questionId,
        List<AssessmentAnswer> answers)
    {

        if (answers.Count == 0) return 0;
        
        var client = _restService.GetClient();

        foreach (var answer in answers)
        {
            var request = new RestRequest($"/Assessments/{assessmentId}/Questions/{questionId}/Answers/{answer.Id}");
            var result = client.Delete<string>(request);
            if (result != "Ok")
            {
                _logger.Error("Error deleting answer {0}" , answer.Id);
                return -1;
            }
        }

        return 0;
    }
    
    public Tuple<int, AssessmentQuestion?> CreateQuestion(int assessmentId, AssessmentQuestion question)
    {
        var client = _restService.GetClient();
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
            _logger.Error("Error creating assessment question: {0}", ex.Message);
            return new Tuple<int, AssessmentQuestion?>(-1, null);
        }
    }
    
    public Tuple<int, AssessmentQuestion?> UpdateQuestion(int assessmentId, AssessmentQuestion question)
    {
        var client = _restService.GetClient();
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
            _logger.Error("Error updating assessment question: {0}", ex.Message);
            return new Tuple<int, AssessmentQuestion?>(-1, null);
        }
    }
    
    public int DeleteQuestion(int assessmentId, int assessmentQuestionId)
    {
        var client = _restService.GetClient();
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
            _logger.Error("Error deleting assessment question: {0} message: {1}", assessmentId, ex.Message);
            return -1;
        }
    }
    
    public int Delete(int assessmentId)
    {
        var client = _restService.GetClient();
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
            _logger.Error("Error deleting assessment: {0} message: {1}", assessmentId, ex.Message);
            return -1;
        }
    }
    
    public List<AssessmentQuestion>? GetAssessmentQuestions(int assessmentId)
    {
        var client = _restService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Questions");

        try
        {
            var response = client.Get<List<AssessmentQuestion>>(request);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error("Error getting assessments questions: {0}", ex.Message);
            return null;
        }
    }
    
    public List<AssessmentAnswer>? GetAssessmentAnswers(int assessmentId)
    {
        var client = _restService.GetClient();
        var request = new RestRequest($"/Assessments/{assessmentId}/Answers");

        try
        {
            var response = client.Get<List<AssessmentAnswer>>(request);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error("Error getting assessments answers: {0}", ex.Message);
            return null;
        }
    }

}