﻿// -----------------------------------------------------------------------
//  <copyright file="EntityConfigurationTypeFinder.cs" company="OSharp开源团队">
//      Copyright (c) 2014-2017 OSharp. All rights reserved.
//  </copyright>
//  <site>http://www.osharp.org</site>
//  <last-editor>郭明锋</last-editor>
//  <last-date>2017-08-17 2:53</last-date>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using OSharp.Dependency;
using OSharp.Exceptions;
using OSharp.Finders;


namespace OSharp.Entity
{
    /// <summary>
    /// 实体类配置类型查找器
    /// </summary>
    public class EntityConfigurationTypeFinder : FinderBase<Type>, IEntityConfigurationTypeFinder, ISingletonDependency
    {
        private readonly IEntityConfigurationAssemblyFinder _assemblyFinder;
        private Dictionary<Type, IEntityRegister[]> _entityRegistersDict;

        /// <summary>
        /// 获取 各个上下文的实体注册信息字典
        /// </summary>
        protected Dictionary<Type, IEntityRegister[]> EntityRegistersDict
        {
            get
            {
                if (_entityRegistersDict == null)
                {
                    FindAll(true);
                }
                return _entityRegistersDict;
            }
        }

        /// <summary>
        /// 初始化一个<see cref="EntityConfigurationTypeFinder"/>类型的新实例
        /// </summary>
        public EntityConfigurationTypeFinder(IEntityConfigurationAssemblyFinder assemblyFinder)
        {
            _assemblyFinder = assemblyFinder;
        }

        /// <summary>
        /// 重写以实现所有项的查找
        /// </summary>
        /// <returns></returns>
        protected override Type[] FindAllItems()
        {
            Type baseType = typeof(IEntityRegister);
            Type[] types = _assemblyFinder.FindAll()
                .SelectMany(assembly => assembly.GetTypes().Where(type => baseType.IsAssignableFrom(type) && !type.IsAbstract))
                .ToArray();
            EntityRegistersInit(types);
            return types;
        }

        /// <summary>
        /// 初始化实体映射对象字典
        /// </summary>
        /// <param name="types"></param>
        private void EntityRegistersInit(Type[] types)
        {
            if (types.Length == 0)
            {
                return;
            }
            List<IEntityRegister> registers = types.Select(type => Activator.CreateInstance(type) as IEntityRegister).ToList();
            Dictionary<Type, IEntityRegister[]> dict = new Dictionary<Type, IEntityRegister[]>();
            List<IGrouping<Type, IEntityRegister>> groups = registers.GroupBy(m => m.DbContextType).ToList();
            foreach (IGrouping<Type, IEntityRegister> group in groups)
            {
                Type key = group.Key ?? typeof(DefaultDbContext);
                List<IEntityRegister> list = new List<IEntityRegister>();
                if (group.Key == null || group.Key == typeof(DefaultDbContext))
                {
                    list.AddRange(group);
                }
                else
                {
                    list = group.ToList();
                }
                if (list.Count > 0)
                {
                    dict[key] = list.ToArray();
                }
            }
            _entityRegistersDict = dict;
        }

        /// <summary>
        /// 获取指定上下文类型的实体配置注册信息
        /// </summary>
        /// <param name="dbContextType">数据上下文类型</param>
        /// <returns></returns>
        public IEntityRegister[] GetEntityRegisters(Type dbContextType)
        {
            return EntityRegistersDict.ContainsKey(dbContextType) ? EntityRegistersDict[dbContextType] : new IEntityRegister[0];
        }

        /// <summary>
        /// 获取 实体类所属的数据上下文类
        /// </summary>
        /// <param name="entityType">实体类型</param>
        /// <returns>数据上下文类型</returns>
        public Type GetDbContextTypeForEntity(Type entityType)
        {
            foreach (var item in EntityRegistersDict)
            {
                if (item.Value.Any(m => m.EntityType == entityType))
                {
                    return item.Key;
                }
            }
            throw new OsharpException($"无法获取实体类“{entityType}”的所属上下上下文类型");
        }
    }
}