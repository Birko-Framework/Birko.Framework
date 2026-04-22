# Birko.Workflow.XML

## Overview
XML file-based persistence for workflow instances. Uses `AsyncXmlStore` from Birko.Data.XML to store workflow state, supporting Save, Load, Delete, and query-by-state/status/workflow-name operations. Suitable for development, testing, and single-process deployments.

## Project Location
`C:\Source\Birko.Workflow.XML\` — Shared project (.shproj + .projitems)

## Components
- **Models/XmlWorkflowInstanceModel.cs** — XML-serializable workflow instance model extending AbstractModel. Holds WorkflowName, CurrentState, Status, DataXml, HistoryXml, timestamps. Converts to/from WorkflowInstance<TData> via ISerializer (defaults to SystemXmlSerializer). Uses [XmlRoot]/[XmlElement] attributes.
- **XmlWorkflowInstanceStore.cs** — IWorkflowInstanceStore<TData> implementation. Delegates to AsyncXmlStore<XmlWorkflowInstanceModel> for Save, Load, Delete, FindByState, FindByStatus, FindByWorkflowName. Supports construction with Settings or pre-built store.
- **XmlWorkflowInstanceSchema.cs** — Static helper for schema management: EnsureCreatedAsync and DropAsync (initialize/destroy the XML file).

## Dependencies
- Birko.Data.XML.Stores (AsyncXmlStore<T>)
- Birko.Data.Stores (OrderBy<T>)
- Birko.Configuration (Settings)
- Birko.Workflow.Core (WorkflowInstance<TData>, WorkflowStatus, StateChangeRecord, IWorkflowInstanceStore)
- Birko.Serialization (ISerializer, used for DataXml/HistoryXml round-trip)
- Birko.Data.Core (AbstractModel)
- System.Xml.Serialization

## Maintenance
When modifying this project, update this CLAUDE.md, README.md, and root CLAUDE.md.
