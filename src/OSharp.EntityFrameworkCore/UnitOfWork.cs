﻿// -----------------------------------------------------------------------
//  <copyright file="UnitOfWork.cs" company="OSharp开源团队">
//      Copyright (c) 2014-2017 OSharp. All rights reserved.
//  </copyright>
//  <site>http://www.osharp.org</site>
//  <last-editor>郭明锋</last-editor>
//  <last-date>2017-08-21 22:20</last-date>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using OSharp.Data;
using OSharp.Dependency;
using OSharp.Entity.Transactions;
using OSharp.Exceptions;
using OSharp.Extensions;


namespace OSharp.Entity
{
    /// <summary>
    /// 业务单元操作
    /// </summary>
    public class UnitOfWork : IUnitOfWork, IScopeDependency
    {
        private readonly IServiceProvider _serviceProvider;
        private List<DbContextResolveOptions> _optionsConfigs = new List<DbContextResolveOptions>();

        /// <summary>
        /// 初始化一个<see cref="UnitOfWork"/>类型的新实例
        /// </summary>
        public UnitOfWork(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            ActiveTransactionInfos = new Dictionary<string, ActiveTransactionInfo>();
        }

        /// <summary>
        /// 获取 活动的事务信息字典，以连接字符串为健，活动事务信息为值
        /// </summary>
        protected IDictionary<string, ActiveTransactionInfo> ActiveTransactionInfos { get; }

        /// <summary>
        /// 获取指定数据上下文类型<typeparamref name="TEntity"/>的实例
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <typeparam name="TKey">实体主键类型</typeparam>
        /// <returns><typeparamref name="TEntity"/>所属上下文类的实例</returns>
        public IDbContext GetDbContext<TEntity, TKey>() where TEntity : IEntity<TKey> where TKey : IEquatable<TKey>
        {
            IEntityConfigurationTypeFinder typeFinder = _serviceProvider.GetService<IEntityConfigurationTypeFinder>();
            Type dbContextType = typeFinder.GetDbContextTypeForEntity(typeof(TEntity));

            DbContext dbContext;
            OsharpDbContextConfig dbContextConfig = GetDbContextResolveOptionsConfig(dbContextType);
            DbContextResolveOptions resolveOptions = new DbContextResolveOptions(dbContextConfig);
            IDbContextResolver contextResolver = _serviceProvider.GetService<IDbContextResolver>();
            ActiveTransactionInfo transInfo = ActiveTransactionInfos.GetOrDefault(resolveOptions.ConnectionString);
            //连接字符串的事务不存在，添加起始上下文事务信息
            if (transInfo == null)
            {
                resolveOptions.ExistingConnection = null;
                dbContext = contextResolver.Resolve(resolveOptions);
                IDbContextTransaction transaction = dbContext.Database.BeginTransaction();
                transInfo = new ActiveTransactionInfo(transaction, dbContext);
                ActiveTransactionInfos[resolveOptions.ConnectionString] = transInfo;
            }
            else
            {
                resolveOptions.ExistingConnection = transInfo.DbContextTransaction.GetDbTransaction().Connection;
                dbContext = contextResolver.Resolve(resolveOptions);
                if (dbContext.IsRelationalTransaction())
                {
                    dbContext.Database.UseTransaction(transInfo.DbContextTransaction.GetDbTransaction());
                }
                else
                {
                    dbContext.Database.BeginTransaction();
                }
                transInfo.AttendedDbContexts.Add(dbContext);
            }
            return dbContext as IDbContext;
        }

        /// <summary>
        /// 提交当前上下文的事务更改
        /// </summary>
        public void Commit()
        {
            foreach (ActiveTransactionInfo transInfo in ActiveTransactionInfos.Values)
            {
                transInfo.DbContextTransaction.Commit();

                foreach (DbContext attendedDbContext in transInfo.AttendedDbContexts)
                {
                    if (attendedDbContext.IsRelationalTransaction())
                    {
                        //关系型数据库共享事务
                        continue;
                    }
                    attendedDbContext.Database.CommitTransaction();
                }
            }
        }

        private OsharpDbContextConfig GetDbContextResolveOptionsConfig(Type dbContextType)
        {
            IOsharpConfigProvider osharpConfigProvider = _serviceProvider.GetService<IOsharpConfigProvider>();
            OsharpConfig osharpConfig = osharpConfigProvider.Create();
            OsharpDbContextConfig dbContextConfig = osharpConfig.DbContexts.Values.SingleOrDefault(m => m.DbContextType == dbContextType);
            if (dbContextConfig == null)
            {
                throw new OsharpException($"无法找到数据上下文“{dbContextType}”的配置信息");
            }
            return dbContextConfig;
        }

        /// <summary>释放对象.</summary>
        public void Dispose()
        {
            foreach (ActiveTransactionInfo transInfo in ActiveTransactionInfos.Values)
            {
                transInfo.DbContextTransaction.Dispose();
                foreach (DbContext attendedDbContext in transInfo.AttendedDbContexts)
                {
                    attendedDbContext.Dispose();
                }
                transInfo.StarterDbContext.Dispose();
            }
            ActiveTransactionInfos.Clear();
        }
    }
}