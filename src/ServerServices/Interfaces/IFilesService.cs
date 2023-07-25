﻿using DAL.Entities;
using Model.DTO;
using File = DAL.Entities.File;

namespace ServerServices.Interfaces;

public interface IFilesService
{
    /// <summary>
    /// List all files
    /// </summary>
    /// <returns>List of files</returns>
    public List<FileListing> GetAll();
    
    /// <summary>
    /// Get´s the file by unique name
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public File GetByUniqueName(string name);
    
    /// <summary>
    /// Creates a new file
    /// </summary>
    /// <param name="file">the file object</param>
    /// <param name="creatingUser">The user creating the file</param>
    /// <returns></returns>
    public File Create(File file, User creatingUser);
}