﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Web.Management.Server;
using RichardSzalay.HostsFileExtension.Messages;
using Microsoft.Web.Administration;

namespace RichardSzalay.HostsFileExtension.Service
{
    public class ManageHostFileModuleService : ModuleService
    {
        private readonly IAddressProvider addressProvider;

        public ManageHostFileModuleService()
            : this(new DnsAddressProvider())
        {
        }

        public ManageHostFileModuleService(IAddressProvider addressProvider)
        {
            this.addressProvider = addressProvider;
        }

        [ModuleServiceMethod(PassThrough = true)]
        public PropertyBag GetEntries(PropertyBag bag)
        {
            HostsFile hostsFile = GetHostsFile();

            IList<HostEntry> entries = hostsFile.Entries.ToList();

            var response = new GetEntriesResponse(entries);

            return response.ToPropertyBag();
        }

        [ModuleServiceMethod(PassThrough = true)]
        public PropertyBag AddEntries(PropertyBag bag)
        {
            return CatchCommonExceptions(() =>
                {
                    AddEntriesRequest request = new AddEntriesRequest(bag);

                    IList<HostEntry> hostEntries = request.Entries;

                    HostsFile hostsFile = GetHostsFile();

                    foreach (HostEntry hostEntry in hostEntries)
                    {
                        hostsFile.AddEntry(hostEntry);
                    }

                    hostsFile.Save();

                    return new AddEntriesResponse().ToPropertyBag();
                });
        }

        [ModuleServiceMethod(PassThrough = true)]
        public PropertyBag EditEntries(PropertyBag bag)
        {
            return CatchCommonExceptions(() =>
                {
                    EditEntriesRequest request = new EditEntriesRequest(bag);

                    HostsFile hostsFile = GetHostsFile();

                    IEnumerable<HostEntry> hostEntries = hostsFile.Entries;

                    for (int i = 0; i < request.ChangedEntries.Count; i++)
                    {
                        HostEntry originalEntry = request.OriginalEntries[i];
                        HostEntry changedEntry = request.ChangedEntries[i];

                        HostEntry hostEntry = FindHostEntry(originalEntry, hostEntries);

                        hostEntry.Address = changedEntry.Address;
                        hostEntry.Hostname = changedEntry.Hostname;
                        hostEntry.Comment = changedEntry.Comment;
                        hostEntry.Enabled = changedEntry.Enabled;
                    }

                    hostsFile.Save();

                    return new EditEntriesResponse().ToPropertyBag();
                });
        }

        [ModuleServiceMethod(PassThrough = true)]
        public PropertyBag DeleteEntries(PropertyBag bag)
        {
            return CatchCommonExceptions(() =>
            {
                DeleteEntriesRequest request = new DeleteEntriesRequest(bag);

                HostsFile hostsFile = GetHostsFile();

                IEnumerable<HostEntry> hostEntries = hostsFile.Entries;

                foreach(HostEntry remoteEntry in request.Entries)
                {
                    HostEntry localEntry = FindHostEntry(remoteEntry, hostEntries);

                    hostsFile.DeleteEntry(localEntry);
                }

                hostsFile.Save();

                return new DeleteEntriesResponse().ToPropertyBag();
            });
        }

        private HostEntry FindHostEntry(HostEntry entryToFind, IEnumerable<HostEntry> entries)
        {
            var entry = entries.FirstOrDefault(e => entryToFind.Line == e.Line);

            if (entry == null || entryToFind.ToString() != entry.ToString())
            {
                throw new HostsFileEditCollisionException();
            }

            foreach (HostEntry hostEntry in entries)
            {
                if (entryToFind.ToString() == hostEntry.ToString())
                {
                    return hostEntry;
                }
            }

            throw new HostsFileServiceException("File has changed. Please reload");
        }

        private PropertyBag CatchCommonExceptions(Func<PropertyBag> func)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                return ServiceMessage.CreateError(ex.Message);
            }
        }

        private HostsFile GetHostsFile()
        {
            return new HostsFile();
        }

        [ModuleServiceMethod(PassThrough = true)]
        public PropertyBag GetSiteBindings(string siteName)
        {
            return CatchCommonExceptions(() =>
                {
                    Site site = ManagementUnit.ReadOnlyServerManager.Sites[siteName];

                    if (site == null)
                    {
                        return ServiceMessage.CreateError("Site not found");
                    }

                    var bindings = site.Bindings;
                    int bindingCount = bindings.Count;
                    List<SiteBinding> siteBindings = new List<SiteBinding>(bindingCount);

                    for (int i = 0; i < bindingCount; i++)
                    {
                        var binding = bindings[i];

                        if (IsValidBinding(binding))
                        {
                            siteBindings.Add(MapBindingToSiteBinding(binding));
                        }
                    }

                    return new GetSiteBindingHostnamesResponse(siteBindings.ToArray()).ToPropertyBag();
                });
        }

        [ModuleServiceMethod(PassThrough = true)]
        public PropertyBag GetServerAddresses()
        {
            string[] addresses = addressProvider.GetAddresses();

            return new GetServerAddressesResponse(addresses).ToPropertyBag();
        }

        private SiteBinding MapBindingToSiteBinding(Binding binding)
        {
            return new SiteBinding
            {
                Host = binding.Host,
                Address = binding.BindingInformation.StartsWith("*")
                    ? "*"
                    : binding.EndPoint.ToString()
            };
        }

        private bool IsValidBinding(Binding binding)
        {
            return binding.IsIPPortHostBinding &&
                !String.IsNullOrEmpty(binding.Host);
        }

        private Site GetSite(SiteCollection sites, string name)
        {
            int siteCount = sites.Count;

            for (int i=0; i<siteCount; i++)
            {
                if (sites[i].Name == name)
                {
                    return sites[i];
                }
            }

            return null;
        }
    }
}
